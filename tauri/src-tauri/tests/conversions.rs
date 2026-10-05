use pdf_forge::{markdown::render_markdown, model::MarkdownOptions};
use std::fs;

#[test]
fn rich_markdown_is_safe_and_offline() {
    let dir = tempfile::tempdir().unwrap();
    let source = dir.path().join("note.md");
    fs::write(&source, "# Title\n\n## Section\n\n|a|b|\n|-|-|\n|1|2|\n\n- [x] done\n\n$x^2$\n\n```mermaid\ngraph TD; A-->B\n```\n<script>alert(1)</script>\n[bad](javascript:alert(1))").unwrap();
    let options = MarkdownOptions { toc: true, header: "<script>".into(), ..Default::default() };
    let html = render_markdown(&source, &options).unwrap();
    assert!(html.contains("<table>"));
    assert!(html.contains("type=\"checkbox\""));
    assert!(html.contains("class=\"math-inline\""));
    assert!(html.contains("class=\"mermaid\""));
    assert!(html.contains("href=\"#heading-2\""));
    assert!(!html.contains("<script>alert"));
    assert!(!html.contains("href=\"javascript:"));
    assert!(html.contains("&lt;script&gt;"));
}

#[test]
fn image_cannot_escape_document_folder_or_use_network() {
    let dir = tempfile::tempdir().unwrap();
    let source = dir.path().join("note.md");
    for target in ["https://example.invalid/a.png", "../outside.png", "file:///C:/secret.png", "data:image/png;base64,AA=="] {
        fs::write(&source, format!("![img]({target})")).unwrap();
        assert!(render_markdown(&source, &MarkdownOptions::default()).is_err(), "{target}");
    }
}

#[test]
fn local_image_is_embedded_and_duplicate_headings_are_unique() {
    let dir = tempfile::tempdir().unwrap();
    fs::write(dir.path().join("image.png"), [137,80,78,71,13,10,26,10]).unwrap();
    let source = dir.path().join("note.md");
    fs::write(&source, "# Same\n# Same\n![alt](image.png)").unwrap();
    let html = render_markdown(&source, &MarkdownOptions { toc: true, ..Default::default() }).unwrap();
    assert!(html.contains("data:image/png;base64,"));
    assert!(html.contains("id=\"heading-1\""));
    assert!(html.contains("id=\"heading-2\""));
}

#[test]
fn docx_requires_all_package_parts_and_well_formed_xml() {
    use std::io::{Read, Write};
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("document.docx");
    let mut zip = zip::ZipWriter::new(fs::File::create(&path).unwrap());
    for (name, content) in [("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>"), ("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>"), ("word/document.xml", "<document xmlns=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><body/></document>")] {
        zip.start_file(name, zip::write::SimpleFileOptions::default()).unwrap();
        zip.write_all(content.as_bytes()).unwrap();
    }
    zip.finish().unwrap();
    pdf_forge::conversions::validate_docx(&path).unwrap();
    let bad = dir.path().join("bad.docx");
    fs::write(&bad, "not a ZIP").unwrap();
    assert!(pdf_forge::conversions::validate_docx(&bad).is_err());
    let mut zip = zip::ZipWriter::new(fs::File::create(&bad).unwrap());
    zip.start_file("word/document.xml", zip::write::SimpleFileOptions::default()).unwrap();
    zip.write_all(b"<document>").unwrap();
    zip.finish().unwrap();
    assert!(pdf_forge::conversions::validate_docx(&bad).is_err());
    for mutation in 0..3 {
        let mut original = zip::ZipArchive::new(fs::File::open(&path).unwrap()).unwrap();
        let mut modified = zip::ZipWriter::new(fs::File::create(&bad).unwrap());
        for name in ["[Content_Types].xml", "_rels/.rels", "word/document.xml"] {
            let mut content = String::new();
            original.by_name(name).unwrap().read_to_string(&mut content).unwrap();
            if mutation == 0 && name == "word/document.xml" { content = "<document>".into(); }
            if mutation == 1 && name == "_rels/.rels" { content = content.replace("Target=", "TargetMode=\"External\" Target="); }
            if mutation == 2 && name == "[Content_Types].xml" { content = content.replace(".document.main+xml", ".template.main+xml"); }
            modified.start_file(name, zip::write::SimpleFileOptions::default()).unwrap();
            modified.write_all(content.as_bytes()).unwrap();
        }
        modified.finish().unwrap();
        assert!(pdf_forge::conversions::validate_docx(&bad).is_err(), "mutation {mutation}");
    }
}

#[test]
fn print_margin_content_cannot_inject_styles_or_scripts() {
    let dir = tempfile::tempdir().unwrap();
    let source = dir.path().join("note.md");
    fs::write(&source, "# Document").unwrap();
    let html = render_markdown(&source, &MarkdownOptions { header: "\"}</style><script>alert(1)</script>".into(), page_numbers: true, ..Default::default() }).unwrap();
    assert!(!html.contains("</style><script>"));
    assert!(html.contains("counter(page)"));
    assert!(html.contains("size:A4;margin:18mm"));
}
