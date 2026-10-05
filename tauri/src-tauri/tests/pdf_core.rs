use lopdf::{dictionary, Document, Object, Stream};
use pdf_forge::pdf;
use sha2::{Digest, Sha256};
use std::path::Path;

fn fixture(path: &Path, count: u32, label: &str) {
    let mut d = Document::with_version("1.7");
    let pages = d.new_object_id();
    let font = d.add_object(dictionary! {"Type"=>"Font", "Subtype"=>"Type1", "BaseFont"=>"Helvetica"});
    let mut ids = Vec::new();
    for n in 1..=count {
        let text = format!("BT /F1 16 Tf 30 740 Td ({label}-PAGE-{n:02}) Tj 0 -30 Td (Chapter {n}: rich fixture) Tj 0 -30 Td (Paragraph containing /Title and /Author tokens.) Tj 0 -30 Td (Table: item quantity price) Tj ET");
        let content = d.add_object(Stream::new(dictionary!{},text.into_bytes()));
        let uri = d.add_object(dictionary!{"Type"=>"Annot", "Subtype"=>"Link", "Rect"=>vec![30.into(),650.into(),250.into(),680.into()], "A"=>dictionary!{"S"=>"URI", "URI"=>Object::string_literal("https://example.invalid/test")}});
        ids.push(d.add_object(dictionary!{"Type"=>"Page", "Parent"=>pages,"Contents"=>content,"Annots"=>vec![uri.into()]}));
    }
    let internal = d.add_object(dictionary!{"Type"=>"Annot", "Subtype"=>"Link", "Rect"=>vec![30.into(),600.into(),250.into(),620.into()], "Dest"=>vec![ids[0].into(),Object::Name(b"Fit".to_vec())]});
    d.get_object_mut(ids[1]).unwrap().as_dict_mut().unwrap().get_mut(b"Annots").unwrap().as_array_mut().unwrap().push(internal.into());
    let named=d.add_object(dictionary!{"Type"=>"Annot","Subtype"=>"Link","Rect"=>vec![30.into(),550.into(),250.into(),580.into()],"A"=>dictionary!{"S"=>"GoTo","D"=>Object::string_literal("start")}});
    d.get_object_mut(ids[2]).unwrap().as_dict_mut().unwrap().get_mut(b"Annots").unwrap().as_array_mut().unwrap().push(named.into());
    let bookmark_root=d.new_object_id();
    let bookmark=d.add_object(dictionary!{"Title"=>Object::string_literal("Start"),"Parent"=>bookmark_root,"Dest"=>vec![ids[0].into(),Object::Name(b"Fit".to_vec())]});
    d.objects.insert(bookmark_root,dictionary!{"Type"=>"Outlines","First"=>bookmark,"Last"=>bookmark,"Count"=>1}.into());
    d.objects.insert(pages,dictionary!{"Type"=>"Pages","Kids"=>ids.iter().copied().map(Object::Reference).collect::<Vec<_>>(),"Count"=>count,"MediaBox"=>vec![0.into(),0.into(),612.into(),792.into()],"CropBox"=>vec![10.into(),10.into(),600.into(),780.into()],"Rotate"=>90,"Resources"=>dictionary!{"Font"=>dictionary!{"F1"=>font}}}.into());
    let xmp=d.add_object(Stream::new(dictionary!{"Type"=>"Metadata","Subtype"=>"XML"},b"secret-xmp-title".to_vec()));
    let catalog=d.add_object(dictionary!{"Type"=>"Catalog","Pages"=>pages,"Metadata"=>xmp,"Outlines"=>bookmark_root,"Names"=>dictionary!{"Dests"=>dictionary!{"Names"=>vec![Object::string_literal("start"), Object::Array(vec![ids[0].into(),Object::Name(b"Fit".to_vec())])]}}});
    let info=d.add_object(dictionary!{"Title"=>Object::string_literal("secret-info-title"),"Custom"=>Object::string_literal("keep-custom")});
    d.trailer.set("Root",catalog); d.trailer.set("Info",info);d.trailer.set("ID",vec![Object::string_literal(vec![1u8;16]),Object::string_literal(vec![1u8;16])]);
    d.save(path).unwrap();
}
fn hash(path: &Path)->Vec<u8>{ Sha256::digest(std::fs::read(path).unwrap()).to_vec() }

#[test]
fn split_rich_forty_pages_into_six_without_mutating_source() {
    let t=tempfile::tempdir().unwrap(); let source=t.path().join("source.pdf"); fixture(&source,40,"SPLIT"); let before=hash(&source);
    assert_eq!(pdf::info(&source).unwrap().pages,40);
    let mut first=1;
    for (i,last) in [7,14,21,28,34,40].into_iter().enumerate(){
        let output=t.path().join(format!("part-{i}.pdf")); assert_eq!(pdf::split_to(&source,first,last,&output).unwrap(),last-first+1);
        let d=Document::load(&output).unwrap(); assert_eq!(pdf::validate(&output).unwrap(),last-first+1);
        for (n,id) in d.get_pages(){
            let page=d.get_object(id).unwrap().as_dict().unwrap(); assert_eq!(page.get(b"Rotate").unwrap().as_i64().unwrap(),90);
            assert_eq!(page.get(b"MediaBox").unwrap().as_array().unwrap().len(),4);
            assert_eq!(page.get(b"CropBox").unwrap().as_array().unwrap().len(),4);
            assert!(d.extract_text(&[n]).unwrap().contains(&format!("SPLIT-PAGE-{:02}",first+n-1)));
            let text=d.extract_text(&[n]).unwrap();assert!(text.contains("/Title and /Author"));assert!(text.contains("Table: item quantity price"));
        }
        let bytes=std::fs::read(&output).unwrap(); assert!(!bytes.windows(b"secret-info-title".len()).any(|s|s==b"secret-info-title")); assert!(!bytes.windows(b"secret-xmp-title".len()).any(|s|s==b"secret-xmp-title"));
        let cat=d.catalog().unwrap(); assert_eq!(cat.has(b"Outlines"),i==0);
        let info=d.get_object(d.trailer.get(b"Info").unwrap().as_reference().unwrap()).unwrap().as_dict().unwrap();assert!(!info.has(b"Title"));assert_eq!(info.get(b"Custom").unwrap().as_str().unwrap(),b"keep-custom");
        let names=cat.get(b"Names").unwrap().as_dict().unwrap();assert_eq!(names.has(b"Dests"),i==0);
        let first_page=*d.get_pages().get(&1).unwrap(); let annotations=d.get_object(first_page).unwrap().as_dict().unwrap().get(b"Annots").unwrap().as_array().unwrap();
        let uri=d.get_object(annotations[0].as_reference().unwrap()).unwrap().as_dict().unwrap().get(b"A").unwrap().as_dict().unwrap();assert_eq!(uri.get(b"URI").unwrap().as_str().unwrap(),b"https://example.invalid/test");
        first=last+1;
    }
    assert_eq!(before,hash(&source));
}
#[test]
fn split_prunes_internal_links_whose_target_was_removed(){
    let t=tempfile::tempdir().unwrap();let source=t.path().join("source.pdf");fixture(&source,40,"NAV");let out=t.path().join("part.pdf");pdf::split_to(&source,2,21,&out).unwrap();let d=Document::load(out).unwrap();let id=d.get_pages()[&1];
    assert_eq!(d.get_object(id).unwrap().as_dict().unwrap().get(b"Annots").unwrap().as_array().unwrap().len(),1);
    assert!(!d.catalog().unwrap().has(b"Outlines"));
}
#[test]
fn unlock_requires_known_password_and_detects_empty_user_permissions(){
    use lopdf::{EncryptionState,EncryptionVersion,Permissions};
    let t=tempfile::tempdir().unwrap();
    for user in ["fixture-user",""]{
        let plain=t.path().join(format!("plain-{}.pdf",user.len()));fixture(&plain,20,"LOCK");let encrypted=t.path().join(format!("locked-{}.pdf",user.len()));let mut d=Document::load(&plain).unwrap();
        let state=EncryptionState::try_from(EncryptionVersion::V2{document:&d,owner_password:"fixture-owner",user_password:user,key_length:128,permissions:Permissions::PRINTABLE}).unwrap();d.encrypt(&state).unwrap();d.save(&encrypted).unwrap();let before=hash(&encrypted);
        assert!(pdf::info(&encrypted).unwrap().encrypted);assert!(pdf::merge_to(&[encrypted.clone()],&t.path().join("reject.pdf")).is_err());assert!(pdf::unlock_to(&encrypted,"wrong-password",&t.path().join("wrong.pdf")).is_err());
        let unlocked=t.path().join(format!("unlocked-{}.pdf",user.len()));assert_eq!(pdf::unlock_to(&encrypted,user,&unlocked).unwrap(),20);assert!(!pdf::info(&unlocked).unwrap().encrypted);let output=Document::load(unlocked).unwrap();let text=output.extract_text(&[1]).unwrap();assert!(text.contains("LOCK-PAGE-01"),"user={user:?}, extracted={text:?}");
        let original=Document::load(&plain).unwrap();for(number,id)in output.get_pages(){assert_eq!(output.get_page_content(id),original.get_page_content(original.get_pages()[&number]));}assert_eq!(before,hash(&encrypted));
    }
}
#[test]
fn rejects_cyclic_page_inheritance_without_writing_output(){
    let t=tempfile::tempdir().unwrap();let source=t.path().join("cyclic.pdf");fixture(&source,20,"CYCLE");let mut d=Document::load(&source).unwrap();let id=d.get_pages()[&1];d.get_object_mut(id).unwrap().as_dict_mut().unwrap().set("Parent",id);d.save(&source).unwrap();let before=hash(&source);let out=t.path().join("out.pdf");
    assert_eq!(pdf::split_to(&source,1,7,&out).unwrap_err().code,"pdf_invalid");assert!(!out.exists());assert_eq!(before,hash(&source));
}
#[test]
fn merge_preserves_order_and_refuses_existing_outputs(){
    let t=tempfile::tempdir().unwrap(); let a=t.path().join("a.pdf");let b=t.path().join("b.pdf");fixture(&a,20,"A");fixture(&b,20,"B"); let hashes=[hash(&a),hash(&b)];let out=t.path().join("merged.pdf");
    assert_eq!(pdf::merge_to(&[a.clone(),b.clone()],&out).unwrap(),40);let d=Document::load(&out).unwrap();assert!(d.extract_text(&[1]).unwrap().contains("A-PAGE-01"));assert!(d.extract_text(&[21]).unwrap().contains("B-PAGE-01"));
    let names=d.catalog().unwrap().get(b"Names").unwrap().as_dict().unwrap().get(b"Dests").unwrap().as_dict().unwrap().get(b"Names").unwrap().as_array().unwrap();assert_eq!(names.len(),4);
    for(number,prefix)in [(3,"forge0:start"),(23,"forge1:start")]{let page=d.get_object(d.get_pages()[&number]).unwrap().as_dict().unwrap();let annotation=page.get(b"Annots").unwrap().as_array().unwrap()[1].as_reference().unwrap();let dest=d.get_object(annotation).unwrap().as_dict().unwrap().get(b"A").unwrap().as_dict().unwrap().get(b"D").unwrap().as_str().unwrap();assert_eq!(dest,prefix.as_bytes());}
    assert!(pdf::merge_to(&[a.clone()],&a).is_err());assert!(pdf::split_to(&a,1,7,&out).is_err());assert_eq!(hashes,[hash(&a),hash(&b)]);
}
#[test]
fn legacy_and_indirect_named_destinations_resolve_after_split_and_merge(){
    let t=tempfile::tempdir().unwrap();let source=t.path().join("legacy.pdf");fixture(&source,40,"LEGACY");let mut d=Document::load(&source).unwrap();let pages=d.get_pages();let root=d.trailer.get(b"Root").unwrap().as_reference().unwrap();
    d.get_object_mut(root).unwrap().as_dict_mut().unwrap().set("Dests",dictionary!{"legacy"=>vec![pages[&1].into(),Object::Name(b"Fit".to_vec())]});
    for(number,dest)in [(4,Object::Name(b"legacy".to_vec())),(5,Object::Reference(d.add_object(Object::string_literal("start"))))]{let annotation=d.add_object(dictionary!{"Subtype"=>"Link","Rect"=>vec![30.into(),500.into(),250.into(),520.into()],"Dest"=>dest});d.get_object_mut(pages[&number]).unwrap().as_dict_mut().unwrap().get_mut(b"Annots").unwrap().as_array_mut().unwrap().push(annotation.into());}
    d.save(&source).unwrap();let before=hash(&source);let split=t.path().join("split.pdf");pdf::split_to(&source,1,20,&split).unwrap();let merged=t.path().join("merged.pdf");pdf::merge_to(&[split.clone(),split.clone()],&merged).unwrap();
    for(path,cases)in [(&split,vec![(4,"legacy",1),(5,"start",1)]),(&merged,vec![(4,"forge0:legacy",1),(5,"forge0:start",1),(24,"forge1:legacy",21),(25,"forge1:start",21)])]{let d=Document::load(path).unwrap();let pages=d.get_pages();let names=d.catalog().unwrap().get(b"Names").unwrap().as_dict().unwrap().get(b"Dests").unwrap().as_dict().unwrap().get(b"Names").unwrap().as_array().unwrap();
        for(number,key,target)in cases{let annots=d.get_object(pages[&number]).unwrap().as_dict().unwrap().get(b"Annots").unwrap().as_array().unwrap();let annotation=d.get_object(annots.last().unwrap().as_reference().unwrap()).unwrap().as_dict().unwrap();let dest=annotation.get(b"Dest").unwrap();assert_eq!(dest.as_str().unwrap(),key.as_bytes());let entry=names.chunks_exact(2).find(|pair|pair[0].as_str().unwrap()==key.as_bytes()).unwrap();assert_eq!(entry[1].as_array().unwrap()[0].as_reference().unwrap(),pages[&target]);}
    }
    assert_eq!(before,hash(&source));
}
#[test]
fn generated_tag_normalization_keeps_rich_twenty_page_content_and_links(){
    let t=tempfile::tempdir().unwrap();let source=t.path().join("generated.pdf");fixture(&source,20,"GENERATED");let mut d=Document::load(&source).unwrap();let root=d.trailer.get(b"Root").unwrap().as_reference().unwrap();let structure=d.add_object(dictionary!{"Type"=>"StructTreeRoot","K"=>Vec::<Object>::new()});
    let catalog=d.get_object_mut(root).unwrap().as_dict_mut().unwrap();catalog.set("StructTreeRoot",structure);catalog.set("MarkInfo",dictionary!{"Marked"=>true});for id in d.get_pages().values(){d.get_object_mut(*id).unwrap().as_dict_mut().unwrap().set("StructParents",1);}
    d.save(&source).unwrap();let before=hash(&source);let out=t.path().join("normalized.pdf");assert_eq!(pdf::normalize_generated_pdf(&source,&out).unwrap(),20);let d=Document::load(&out).unwrap();assert!(!d.catalog().unwrap().has(b"StructTreeRoot"));assert!(!d.catalog().unwrap().has(b"MarkInfo"));assert!(d.catalog().unwrap().has(b"Outlines"));
    for(n,id)in d.get_pages(){assert!(!d.get_object(id).unwrap().as_dict().unwrap().has(b"StructParents"));assert!(d.extract_text(&[n]).unwrap().contains(&format!("GENERATED-PAGE-{n:02}")));assert!(!d.get_object(id).unwrap().as_dict().unwrap().get(b"Annots").unwrap().as_array().unwrap().is_empty());}
    let merged=t.path().join("merged.pdf");assert_eq!(pdf::merge_to(&[out],&merged).unwrap(),20);assert_eq!(before,hash(&source));
}
