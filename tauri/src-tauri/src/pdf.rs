//! Object-graph PDF operations. Destinations use page object IDs, never guessed offsets.
use crate::model::{ForgeError, ForgeResult, PdfInfo};
use lopdf::{dictionary, Dictionary, Document, Object, ObjectId};
use std::{collections::{BTreeMap, HashSet}, fs::{File, OpenOptions}, io::{BufWriter, Read, Write}, path::{Path, PathBuf}};

pub const PREVIEW_LIMIT: u64 = 256 * 1024 * 1024;

fn error(e: impl ToString) -> ForgeError { ForgeError::new("pdf_invalid", e) }
fn resolve<'a>(d: &'a Document, o: &'a Object) -> ForgeResult<&'a Object> {
    let mut current=o; let mut seen=HashSet::new();
    while let Object::Reference(id)=current { if seen.len()>=64 || !seen.insert(*id){return Err(error("Cyclic or excessively deep PDF reference"));} current=d.get_object(*id).map_err(error)?; }
    Ok(current)
}
fn load(path:&Path)->ForgeResult<Document>{Document::load(path).map_err(error)}
fn encrypted(d:&Document)->bool{d.is_encrypted()||d.encryption_state.is_some()}
fn plain(path:&Path)->ForgeResult<Document>{let d=load(path)?;if encrypted(&d){return Err(ForgeError::new("pdf_encrypted","Unlock this PDF with its known password first"));} Ok(d)}
fn read_preview_handle(file:&mut File, cap:u64)->ForgeResult<Vec<u8>> {
    let size=file.metadata().map_err(|e|ForgeError::new("SOURCE_MISSING",e))?.len();
    if size==0 || size>cap {return Err(ForgeError::new("FILE_SIZE","Preview limit is 256 MiB"));}
    let mut bytes=Vec::new();
    file.take(cap+1).read_to_end(&mut bytes).map_err(|e|ForgeError::new("SOURCE_MISSING",e))?;
    if bytes.is_empty() || bytes.len() as u64>cap {return Err(ForgeError::new("FILE_SIZE","Preview limit is 256 MiB"));}
    Ok(bytes)
}
pub fn preview_bytes(path:&Path)->ForgeResult<Vec<u8>> {
    if !path.extension().unwrap_or_default().eq_ignore_ascii_case("pdf") {return Err(ForgeError::new("FILE_TYPE","Preview requires a PDF"));}
    let mut file=File::open(path).map_err(|e|ForgeError::new("SOURCE_MISSING",e))?;
    read_preview_handle(&mut file,PREVIEW_LIMIT)
}
pub fn info(path:&Path)->ForgeResult<PdfInfo>{
    let d=Document::load_mem(&preview_bytes(path)?).map_err(error)?;
    if encrypted(&d) {return Ok(PdfInfo{pages:0,encrypted:true});}
    // lopdf's iterator may silently truncate malformed trees at the object count.
    // Count and validate the actual Kids graph so it cannot undercount PDF.js.
    fn visit(d:&Document,id:ObjectId,parent:Option<ObjectId>,depth:usize,seen:&mut HashSet<ObjectId>,pages:&mut u32)->ForgeResult<()> {
        if depth>64 || !seen.insert(id) {return Err(error("Repeated or excessively deep PDF page tree"));}
        let node=d.get_object(id).map_err(error)?.as_dict().map_err(error)?;
        if let Some(parent)=parent {
            if node.get(b"Parent").and_then(Object::as_reference).map_err(error)?!=parent {return Err(error("Invalid PDF page parent"));}
        }
        if node.has_type(b"Page") {
            *pages+=1;
            if *pages>1000 {return Err(ForgeError::new("FILE_PAGES","Preview limit is 1000 pages"));}
        } else if node.has_type(b"Pages") {
            let before=*pages;
            for child in resolve(d,node.get(b"Kids").map_err(error)?)?.as_array().map_err(error)? {
                visit(d,child.as_reference().map_err(error)?,Some(id),depth+1,seen,pages)?;
            }
            if node.get(b"Count").and_then(Object::as_i64).map_err(error)?!=(*pages-before) as i64 {return Err(error("PDF page count disagrees with page tree"));}
        } else {return Err(error("Invalid PDF page tree node"));}
        Ok(())
    }
    let root=d.catalog().map_err(error)?.get(b"Pages").and_then(Object::as_reference).map_err(error)?;
    let mut pages=0;visit(&d,root,None,0,&mut HashSet::new(),&mut pages)?;
    if pages==0 {return Err(error("PDF has no readable pages"));}
    Ok(PdfInfo{pages,encrypted:false})
}

// Fail explicitly for structures whose document-level relationship cannot be joined safely.
fn supported(d:&Document)->ForgeResult<()> {
    let c=d.catalog().map_err(error)?;
    for key in [b"AcroForm".as_slice(),b"Collection",b"StructTreeRoot",b"OCProperties"] {
        if c.has(key){return Err(ForgeError::new("pdf_unsupported",format!("Unsupported document structure: {}",String::from_utf8_lossy(key))));}
    }
    Ok(())
}
fn clean_metadata(d:&mut Document) {
    let keys=["Title","Author","Subject","Keywords","Creator","Producer","CreationDate","ModDate","Trapped"];
    if let Ok(info)=d.trailer.get(b"Info").cloned(){
        if let Object::Reference(id)=info{
            if let Ok(o)=d.get_object_mut(id){if let Ok(dict)=o.as_dict_mut(){for k in keys{dict.remove(k.as_bytes());}}}
        }else if let Ok(dict)=d.trailer.get_mut(b"Info").and_then(Object::as_dict_mut){for k in keys{dict.remove(k.as_bytes());}}
    }
    fn scrub(o:&mut Object){match o{
        Object::Dictionary(dict)=>{dict.remove(b"Metadata");for (_,v) in dict.iter_mut(){scrub(v);}},
        Object::Stream(s)=>{s.dict.remove(b"Metadata");for (_,v) in s.dict.iter_mut(){scrub(v);}},
        Object::Array(a)=>for v in a{scrub(v)},_=>{}
    }}
    for o in d.objects.values_mut(){scrub(o)}
    d.trailer.remove(b"ID"); d.trailer.remove(b"Prev"); d.trailer.remove(b"XRefStm");
}
fn save(mut d:Document,path:&Path)->ForgeResult<u32>{
    clean_metadata(&mut d);d.prune_objects();
    let count=check(&d)?;
    // create_new prevents existing files, symlinks and source aliases from being overwritten.
    let mut file=OpenOptions::new().write(true).create_new(true).open(path).map_err(|e|ForgeError::new("output_conflict",e))?;
    write_buffered(&mut d,&mut file)?;file.sync_all().map_err(error)?;Ok(count)
}
fn write_buffered<W:Write>(document:&mut Document,target:&mut W)->ForgeResult<()>{
    let mut target=BufWriter::new(target);
    document.save_to(&mut target).map_err(error)?;
    target.flush().map_err(error)
}
fn check(d:&Document)->ForgeResult<u32>{
    let pages=d.get_pages();if pages.is_empty(){return Err(error("PDF has no readable pages"));}
    fn visit(d:&Document,o:&Object,seen:&mut HashSet<ObjectId>)->ForgeResult<()>{match o{
        Object::Reference(id)=>{if seen.insert(*id){visit(d,d.get_object(*id).map_err(error)?,seen)?;}},
        Object::Array(a)=>for v in a{visit(d,v,seen)?},
        Object::Dictionary(dict)=>for (_,v) in dict.iter(){visit(d,v,seen)?},
        Object::Stream(s)=>for (_,v) in s.dict.iter(){visit(d,v,seen)?},_=>{}
    }Ok(())}
    visit(d,&Object::Dictionary(d.trailer.clone()),&mut HashSet::new())?;
    for id in pages.values(){let page=d.get_object(*id).map_err(error)?.as_dict().map_err(error)?;if !page.has_type(b"Page"){return Err(error("Invalid page tree"));}}
    Ok(pages.len() as u32)
}
pub fn validate(path:&Path)->ForgeResult<u32>{check(&plain(path)?)}

/// Flatten tags only for a converter-owned staging PDF. Callers must never expose this
/// as a general user-input operation: user PDFs retain the explicit unsupported policy.
pub fn normalize_generated_pdf(source:&Path,temp:&Path)->ForgeResult<u32>{
    let mut d=plain(source)?;let root=d.trailer.get(b"Root").map_err(error)?.as_reference().map_err(error)?;
    let catalog=d.get_object_mut(root).map_err(error)?.as_dict_mut().map_err(error)?;
    catalog.remove(b"StructTreeRoot");catalog.remove(b"MarkInfo");
    for id in d.get_pages().values(){d.get_object_mut(*id).map_err(error)?.as_dict_mut().map_err(error)?.remove(b"StructParents");}
    let ids=d.get_pages().values().copied().collect::<Vec<_>>();rebuild_pages(&mut d,&ids)?;
    save(d,temp)
}

fn flatten_page(d:&Document,id:ObjectId)->ForgeResult<Dictionary>{
    let mut page=d.get_object(id).map_err(error)?.as_dict().map_err(error)?.clone();
    let mut parent=page.get(b"Parent").ok().cloned();let mut seen=HashSet::new();
    while let Some(p)=parent{
        if let Object::Reference(id)=p{if !seen.insert(id){return Err(error("Cyclic page tree"));}}
        let dict=resolve(d,&p)?.as_dict().map_err(error)?;
        for key in [b"Resources".as_slice(),b"MediaBox",b"CropBox",b"Rotate"]{if !page.has(key){if let Ok(v)=dict.get(key){page.set(key,v.clone());}}}
        parent=dict.get(b"Parent").ok().cloned();
    }
    if !page.has(b"MediaBox"){return Err(error("Page has no MediaBox"));}Ok(page)
}
fn rebuild_pages(d:&mut Document,ids:&[ObjectId])->ForgeResult<()> {
    let pages=d.new_object_id();let flattened=ids.iter().map(|id|Ok((*id,flatten_page(d,*id)?))).collect::<ForgeResult<Vec<_>>>()?;
    for(id,mut page) in flattened{page.set("Parent",pages);d.objects.insert(id,Object::Dictionary(page));}
    d.objects.insert(pages,dictionary!{"Type"=>"Pages","Count"=>ids.len() as i64,"Kids"=>ids.iter().copied().map(Object::Reference).collect::<Vec<_>>()}.into());
    let root=d.trailer.get(b"Root").map_err(error)?.as_reference().map_err(error)?;
    d.get_object_mut(root).map_err(error)?.as_dict_mut().map_err(error)?.set("Pages",pages);Ok(())
}
fn destination_key(o:&Object)->Option<Vec<u8>>{match o{Object::Name(v)|Object::String(v,_)=>Some(v.clone()),_=>None}}
fn destination_valid(d:&Document,o:&Object,keep:&HashSet<ObjectId>,names:&BTreeMap<Vec<u8>,Object>)->bool{
    fn target(d:&Document,o:&Object,keep:&HashSet<ObjectId>,names:&BTreeMap<Vec<u8>,Object>,depth:usize)->bool{
        if depth>64{return false}let Ok(v)=resolve(d,o)else{return false};
        match v{Object::Array(a)=>a.first().and_then(|v|v.as_reference().ok()).is_some_and(|id|keep.contains(&id)),
            Object::Dictionary(dict)=>dict.get(b"D").is_ok_and(|v|target(d,v,keep,names,depth+1)),
            _=>destination_key(v).and_then(|k|names.get(&k)).is_some_and(|v|target(d,v,keep,names,depth+1))}
    }target(d,o,keep,names,0)
}
fn name_tree(d:&Document,o:&Object,result:&mut BTreeMap<Vec<u8>,Object>,seen:&mut HashSet<ObjectId>)->ForgeResult<()> {
    if let Object::Reference(id)=o{if !seen.insert(*id){return Err(error("Cyclic name tree"));}}
    let dict=resolve(d,o)?.as_dict().map_err(error)?;
    if let Ok(names)=dict.get(b"Names"){let a=resolve(d,names)?.as_array().map_err(error)?;if a.len()%2!=0{return Err(error("Odd name tree"));}for pair in a.chunks_exact(2){let k=destination_key(&pair[0]).ok_or_else(||error("Invalid name tree key"))?;result.insert(k,pair[1].clone());}}
    if let Ok(kids)=dict.get(b"Kids"){for child in resolve(d,kids)?.as_array().map_err(error)?{name_tree(d,child,result,seen)?;}}Ok(())
}
fn destinations(d:&Document)->ForgeResult<BTreeMap<Vec<u8>,Object>>{
    let c=d.catalog().map_err(error)?;let mut result=BTreeMap::new();
    if let Ok(old)=c.get(b"Dests"){for(k,v)in resolve(d,old)?.as_dict().map_err(error)?.iter(){result.insert(k.clone(),v.clone());}}
    if let Ok(names)=c.get(b"Names"){let dict=resolve(d,names)?.as_dict().map_err(error)?;if let Ok(tree)=dict.get(b"Dests"){let mut modern=BTreeMap::new();name_tree(d,tree,&mut modern,&mut HashSet::new())?;for(k,v)in modern{if result.contains_key(&k){return Err(ForgeError::new("pdf_unsupported","Legacy and string destination namespaces contain the same name"));}result.insert(k,v);}}}Ok(result)
}
fn local_navigation_valid(d:&Document,dict:&Dictionary,keep:&HashSet<ObjectId>,names:&BTreeMap<Vec<u8>,Object>)->bool{
    if let Ok(dest)=dict.get(b"Dest"){return destination_valid(d,dest,keep,names)}
    if let Ok(action)=dict.get(b"A"){if let Ok(Object::Dictionary(a))=resolve(d,action){if a.get(b"S").and_then(Object::as_name).ok()==Some(b"GoTo"){return a.get(b"D").is_ok_and(|v|destination_valid(d,v,keep,names));}}}
    true
}
#[derive(Clone)]
struct Outline{dict:Dictionary,children:Vec<Outline>}
fn outlines(d:&Document,first:Option<Object>,keep:&HashSet<ObjectId>,names:&BTreeMap<Vec<u8>,Object>,seen:&mut HashSet<ObjectId>)->ForgeResult<Vec<Outline>>{
    let mut next=first;let mut result=Vec::new();
    while let Some(o)=next{if let Object::Reference(id)=o{if !seen.insert(id){return Err(error("Cyclic outlines"));}}
        let dict=resolve(d,&o)?.as_dict().map_err(error)?.clone();next=dict.get(b"Next").ok().cloned();
        let children=outlines(d,dict.get(b"First").ok().cloned(),keep,names,seen)?;
        if local_navigation_valid(d,&dict,keep,names){result.push(Outline{dict,children});}else{result.extend(children);}
    }Ok(result)
}
fn write_outlines(d:&mut Document,nodes:&[Outline],parent:ObjectId)->(Option<ObjectId>,Option<ObjectId>,i64){
    let ids=nodes.iter().map(|_|d.new_object_id()).collect::<Vec<_>>();let mut total=0;
    for(i,node)in nodes.iter().enumerate(){let mut dict=node.dict.clone();let closed=dict.get(b"Count").and_then(Object::as_i64).unwrap_or(0)<0;
        for k in [b"Parent".as_slice(),b"Prev",b"Next",b"First",b"Last",b"Count"]{dict.remove(k);}dict.set("Parent",parent);
        if i>0{dict.set("Prev",ids[i-1]);}if i+1<ids.len(){dict.set("Next",ids[i+1]);}
        let(first,last,count)=write_outlines(d,&node.children,ids[i]);if let(Some(first),Some(last))=(first,last){dict.set("First",first);dict.set("Last",last);dict.set("Count",if closed{-count}else{count});}total+=1+if closed{0}else{count};
        d.objects.insert(ids[i],dict.into());
    }(ids.first().copied(),ids.last().copied(),total)
}
fn set_outlines(d:&mut Document,nodes:&[Outline])->ForgeResult<()>{
    let root=d.trailer.get(b"Root").map_err(error)?.as_reference().map_err(error)?;let outline=d.new_object_id();let(first,last,count)=write_outlines(d,nodes,outline);
    d.get_object_mut(root).map_err(error)?.as_dict_mut().map_err(error)?.remove(b"Outlines");
    if let(Some(first),Some(last))=(first,last){d.objects.insert(outline,dictionary!{"Type"=>"Outlines","First"=>first,"Last"=>last,"Count"=>count}.into());d.get_object_mut(root).map_err(error)?.as_dict_mut().map_err(error)?.set("Outlines",outline);}Ok(())
}
fn prune_navigation(d:&mut Document,keep:&HashSet<ObjectId>)->ForgeResult<()> {
    let names=destinations(d)?;let valid=names.iter().filter(|(_,v)|destination_valid(d,v,keep,&names)).map(|(k,v)|(k.clone(),v.clone())).collect::<BTreeMap<_,_>>();
    let c=d.catalog().map_err(error)?.clone();let nodes=if let Ok(o)=c.get(b"Outlines"){let dict=resolve(d,o)?.as_dict().map_err(error)?;outlines(d,dict.get(b"First").ok().cloned(),keep,&names,&mut HashSet::new())?}else{Vec::new()};
    for id in keep{
        let page=d.get_object(*id).map_err(error)?.as_dict().map_err(error)?;
        let annots=if let Ok(o)=page.get(b"Annots"){resolve(d,o)?.as_array().map_err(error)?.iter().filter(|a|resolve(d,a).ok().and_then(|v|v.as_dict().ok()).is_some_and(|a|local_navigation_valid(d,a,keep,&names))).cloned().collect::<Vec<_>>()}else{continue};
        d.get_object_mut(*id).map_err(error)?.as_dict_mut().map_err(error)?.set("Annots",annots);
    }
    let mut catalog_names=if let Ok(o)=c.get(b"Names"){resolve(d,o)?.as_dict().map_err(error)?.clone()}else{Dictionary::new()};
    catalog_names.remove(b"Dests");if !valid.is_empty(){let a=valid.into_iter().flat_map(|(k,v)|[Object::string_literal(k),v]).collect::<Vec<_>>();catalog_names.set("Dests",dictionary!{"Names"=>a});}
    let root=d.trailer.get(b"Root").map_err(error)?.as_reference().map_err(error)?;let cat=d.get_object_mut(root).map_err(error)?.as_dict_mut().map_err(error)?;cat.remove(b"Dests");cat.set("Names",catalog_names);
    // Document opening actions may point to omitted pages.
    if let Ok(action)=c.get(b"OpenAction"){let valid=match resolve(d,action)?{Object::Array(_)=>destination_valid(d,action,keep,&names),Object::Dictionary(a)=>local_navigation_valid(d,&dictionary!{"A"=>a.clone()},keep,&names),_=>false};if !valid{d.get_object_mut(root).map_err(error)?.as_dict_mut().map_err(error)?.remove(b"OpenAction");}}
    set_outlines(d,&nodes)?;
    normalize_document_destinations(d,b"")
}
pub fn split_to(source:&Path,first:u32,last:u32,temp:&Path)->ForgeResult<u32>{
    let mut d=plain(source)?;supported(&d)?;let pages=d.get_pages();if first==0||last<first||last>pages.len() as u32{return Err(ForgeError::new("invalid_range","Page range is outside the document"));}
    let ids=pages.range(first..=last).map(|(_,id)|*id).collect::<Vec<_>>();let keep=ids.iter().copied().collect();prune_navigation(&mut d,&keep)?;rebuild_pages(&mut d,&ids)?;save(d,temp)
}

fn rename_destinations(d:&Document,o:&mut Object,prefix:&[u8])->ForgeResult<()>{
    fn rename(d:&Document,v:&mut Object,p:&[u8])->ForgeResult<()>{
        if let Some(key)=destination_key(resolve(d,v)?){let mut name=p.to_vec();name.extend(key);*v=Object::string_literal(name);}Ok(())
    }
    match o{Object::Dictionary(dict)=>{if let Ok(v)=dict.get_mut(b"Dest"){rename(d,v,prefix)?}if dict.get(b"S").and_then(Object::as_name).ok()==Some(b"GoTo"){if let Ok(v)=dict.get_mut(b"D"){rename(d,v,prefix)?}}for(_,v)in dict.iter_mut(){rename_destinations(d,v,prefix)?}},Object::Array(a)=>for v in a{rename_destinations(d,v,prefix)?},Object::Stream(s)=>for(_,v)in s.dict.iter_mut(){rename_destinations(d,v,prefix)?},_=>{}}Ok(())
}
fn normalize_document_destinations(d:&mut Document,prefix:&[u8])->ForgeResult<()>{
    // Resolve against the original graph, then apply only modified objects. Indirect
    // strings may be shared with non-navigation content and must not be edited in place.
    let mut changed=Vec::new();for(id,o)in &d.objects{let mut copy=o.clone();rename_destinations(d,&mut copy,prefix)?;if copy!=*o{changed.push((*id,copy));}}
    for(id,o)in changed{d.objects.insert(id,o);}Ok(())
}
pub fn merge_to(sources:&[PathBuf],temp:&Path)->ForgeResult<u32>{
    if sources.is_empty(){return Err(ForgeError::new("empty_sources","No PDFs selected"));}
    let mut merged=Document::with_version("1.7");let mut page_ids=Vec::new();let mut all_names:BTreeMap<Vec<u8>,Object>=BTreeMap::new();let mut all_outlines=Vec::new();let mut other_names=Dictionary::new();let mut custom_info=Dictionary::new();
    let root=merged.add_object(dictionary!{"Type"=>"Catalog"});merged.trailer.set("Root",root);
    for(index,path)in sources.iter().enumerate(){
        let mut d=plain(path)?;supported(&d)?;d.renumber_objects_with(merged.max_id+1);
        let ids=d.get_pages().values().copied().collect::<Vec<_>>();let keep=ids.iter().copied().collect::<HashSet<_>>();prune_navigation(&mut d,&keep)?;
        let names=destinations(&d)?;let c=d.catalog().map_err(error)?.clone();
        let prefix=format!("forge{index}:").into_bytes();let mut nodes=if let Ok(o)=c.get(b"Outlines"){let dict=resolve(&d,o)?.as_dict().map_err(error)?;outlines(&d,dict.get(b"First").ok().cloned(),&keep,&names,&mut HashSet::new())?}else{Vec::new()};
        fn rename_nodes(d:&Document,nodes:&mut [Outline],prefix:&[u8])->ForgeResult<()>{for node in nodes{let mut o=Object::Dictionary(node.dict.clone());rename_destinations(d,&mut o,prefix)?;node.dict=o.as_dict().map_err(error)?.clone();rename_nodes(d,&mut node.children,prefix)?;}Ok(())}
        rename_nodes(&d,&mut nodes,&prefix)?;all_outlines.extend(nodes);
        for(k,mut v)in names{rename_destinations(&d,&mut v,&prefix)?;let mut new=prefix.clone();new.extend(k);all_names.insert(new,v);}
        if let Ok(o)=c.get(b"Names"){let dict=resolve(&d,o)?.as_dict().map_err(error)?;for(k,v)in dict.iter(){if k==b"Dests"{continue}if other_names.has(k){return Err(ForgeError::new("pdf_unsupported","Multiple documents contain the same non-destination name tree"));}other_names.set(k.clone(),v.clone());}}
        for id in &ids{let page=flatten_page(&d,*id)?;d.objects.insert(*id,page.into());}
        normalize_document_destinations(&mut d,&prefix)?;
        clean_metadata(&mut d);
        if let Ok(o)=d.trailer.get(b"Info"){for(k,v)in resolve(&d,o)?.as_dict().map_err(error)?.iter(){let key=if custom_info.has(k){[prefix.clone(),k.clone()].concat()}else{k.clone()};custom_info.set(key,v.clone());}}
        merged.max_id=d.max_id;merged.objects.extend(d.objects);page_ids.extend(ids);
    }
    if !all_names.is_empty(){other_names.set("Dests",dictionary!{"Names"=>all_names.into_iter().flat_map(|(k,v)|[Object::string_literal(k),v]).collect::<Vec<_>>()});}
    merged.get_object_mut(root).map_err(error)?.as_dict_mut().map_err(error)?.set("Names",other_names);
    if !custom_info.is_empty(){let id=merged.add_object(custom_info);merged.trailer.set("Info",id);}
    rebuild_pages(&mut merged,&page_ids)?;set_outlines(&mut merged,&all_outlines)?;save(merged,temp)
}

pub fn unlock_to(source:&Path,password:&str,temp:&Path)->ForgeResult<u32>{
    let initial=load(source)?;if !encrypted(&initial){return Err(ForgeError::new("pdf_not_encrypted","This PDF is not encrypted"));}
    let mut d=Document::load_with_password(source,password).map_err(|_|ForgeError::new("password_invalid","The PDF password is incorrect or encryption is unsupported"))?;
    // The native loader tries the empty user password first. Reconstruct its security
    // dictionary to authenticate the supplied password even for permission-only PDFs.
    let state=d.encryption_state.clone().ok_or_else(||ForgeError::new("password_invalid","The PDF password is incorrect"))?;
    let security=d.add_object(state.encode().map_err(error)?);d.trailer.set("Encrypt",security);
    d.authenticate_password(password).map_err(|_|ForgeError::new("password_invalid","The PDF password is incorrect"))?;
    d.trailer.remove(b"Encrypt");d.objects.remove(&security);d.encryption_state=None;
    let ids=d.get_pages().values().copied().collect::<Vec<_>>();
    rebuild_pages(&mut d,&ids)?;
    save(d,temp)
}

#[cfg(test)]
mod tests {
    #[test]
    fn metadata_rejects_repeated_kids_instead_of_silently_truncating_the_page_tree() {
        let directory=tempfile::tempdir().unwrap();let path=directory.path().join("repeated.pdf");
        let mut document=Document::with_version("1.7");
        document.objects.insert((1,0),dictionary!{"Type"=>"Catalog","Pages"=>(2,0)}.into());
        document.objects.insert((2,0),dictionary!{"Type"=>"Pages","Count"=>1001,"Kids"=>vec![Object::Reference((3,0));1001]}.into());
        document.objects.insert((3,0),dictionary!{"Type"=>"Page","Parent"=>(2,0),"MediaBox"=>vec![0.into(),0.into(),612.into(),792.into()]}.into());
        document.trailer.set("Root",(1,0));document.max_id=3;document.save(&path).unwrap();
        assert!(info(&path).is_err(),"Malformed Kids must not bypass the preview cap by undercounting");
    }
    use super::*;
    use std::io::{self, Write};

    #[test]
    fn preview_reader_rejects_growth_past_cap_on_open_handle() {
        let dir = tempfile::tempdir().unwrap();
        let source = dir.path().join("growth.pdf");
        std::fs::write(&source, b"%PDF-1.7").unwrap();
        let mut file = std::fs::File::open(&source).unwrap();
        std::fs::write(&source, vec![b'x'; 33]).unwrap();
        assert_eq!(read_preview_handle(&mut file, 32).unwrap_err().code, "FILE_SIZE");
    }

    #[test]
    fn metadata_rejects_oversized_and_empty_pdf_without_parsing() {
        let dir = tempfile::tempdir().unwrap();
        let empty = dir.path().join("empty.pdf");
        std::fs::write(&empty, []).unwrap();
        assert_eq!(preview_bytes(&empty).unwrap_err().code, "FILE_SIZE");
        let oversized = dir.path().join("huge.pdf");
        let file = std::fs::File::create(&oversized).unwrap();
        file.set_len(256 * 1024 * 1024 + 1).unwrap();
        assert_eq!(preview_bytes(&oversized).unwrap_err().code, "FILE_SIZE");
    }

    #[derive(Default)]
    struct CountingWriter { calls: usize, bytes: Vec<u8> }
    impl Write for CountingWriter {
        fn write(&mut self, bytes: &[u8]) -> io::Result<usize> {
            self.calls += 1;
            self.bytes.extend_from_slice(bytes);
            Ok(bytes.len())
        }
        fn flush(&mut self) -> io::Result<()> { Ok(()) }
    }

    fn rich_twenty_page_document() -> Document {
        let mut document = Document::with_version("1.7");
        let pages = document.new_object_id();
        let font = document.add_object(dictionary! {"Type"=>"Font", "Subtype"=>"Type1", "BaseFont"=>"Helvetica"});
        let mut page_ids = Vec::new();
        for page_number in 1..=20 {
            let content = format!("BT /F1 16 Tf 30 740 Td (PAGE-{page_number:02} Title) Tj 0 -30 Td (Rich paragraph for page {page_number}.) Tj ET");
            let stream = document.add_object(lopdf::Stream::new(Dictionary::new(), content.into_bytes()));
            page_ids.push(document.add_object(dictionary! {
                "Type"=>"Page", "Parent"=>pages, "Contents"=>stream,
                "MediaBox"=>vec![0.into(), 0.into(), 612.into(), 792.into()],
                "Resources"=>dictionary! {"Font"=>dictionary! {"F1"=>font}}
            }));
        }
        document.objects.insert(pages, dictionary! {"Type"=>"Pages", "Count"=>20, "Kids"=>page_ids.iter().copied().map(Object::Reference).collect::<Vec<_>>()}.into());
        let catalog = document.add_object(dictionary! {"Type"=>"Catalog", "Pages"=>pages});
        document.trailer.set("Root", catalog);
        document
    }

    #[test]
    fn pdf_serialization_batches_filesystem_writes_for_twenty_pages() {
        let mut document = rich_twenty_page_document();
        let mut writer = CountingWriter::default();
        write_buffered(&mut document, &mut writer).unwrap();

        assert!(writer.bytes.starts_with(b"%PDF-"));
        assert!(writer.calls < 100, "expected buffered serialization, got {} underlying writes", writer.calls);
        let saved = Document::load_mem(&writer.bytes).unwrap();
        assert_eq!(saved.get_pages().len(), 20);
        assert!(saved.extract_text(&[20]).unwrap().contains("PAGE-20 Title"));
    }

    #[test]
    fn pdf_serialization_propagates_write_errors() {
        struct FailingWriter;
        impl Write for FailingWriter {
            fn write(&mut self, _: &[u8]) -> io::Result<usize> { Err(io::Error::other("simulated disk write failure")) }
            fn flush(&mut self) -> io::Result<()> { Ok(()) }
        }

        let mut document = rich_twenty_page_document();
        assert!(write_buffered(&mut document, &mut FailingWriter).is_err());
    }

    #[test]
    fn pdf_serialization_propagates_flush_errors() {
        struct FailingFlushWriter;
        impl Write for FailingFlushWriter {
            fn write(&mut self, bytes: &[u8]) -> io::Result<usize> { Ok(bytes.len()) }
            fn flush(&mut self) -> io::Result<()> { Err(io::Error::other("simulated flush failure")) }
        }

        let mut document = rich_twenty_page_document();
        assert!(write_buffered(&mut document, &mut FailingFlushWriter).is_err());
    }

}
