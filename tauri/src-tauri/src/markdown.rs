//! Offline Markdown HTML preparation. Printing is performed in a disposable WebView2 worker.
use crate::model::{ForgeError, ForgeResult, MarkdownOptions};
use base64::{engine::general_purpose::STANDARD, Engine};
use pulldown_cmark::{html, CodeBlockKind, CowStr, Event, Options, Parser, Tag, TagEnd};
use std::{fs, path::{Component, Path}};

pub fn escape(value: &str) -> String {
    value.replace('&', "&amp;").replace('<', "&lt;").replace('>', "&gt;").replace('"', "&quot;").replace('\'', "&#39;")
}
fn css_string(value: &str) -> String {
    let mut result = String::from("\"");
    for ch in value.chars() {
        if matches!(ch, '"' | '\\' | '\n' | '\r' | '<' | '>') { result.push_str(&format!("\\{:x} ", ch as u32)); }
        else { result.push(ch); }
    }
    result.push('"');
    result
}
fn image_url(folder: &Path, url: &str) -> ForgeResult<String> {
    let relative = Path::new(url);
    if url.contains(':') || url.starts_with(['/', '\\']) || relative.components().any(|c| !matches!(c, Component::Normal(_) | Component::CurDir)) {
        return Err(ForgeError::new("markdown_image", "Only relative local images inside the document folder are supported"));
    }
    let path = folder.join(relative).canonicalize().map_err(|e| ForgeError::new("markdown_image", e))?;
    if !path.starts_with(folder) { return Err(ForgeError::new("markdown_image", "Image escapes the document folder")); }
    let bytes = fs::read(&path).map_err(|e| ForgeError::new("markdown_image", e))?;
    if bytes.len() > 32 * 1024 * 1024 { return Err(ForgeError::new("markdown_image", "Image exceeds 32 MiB")); }
    let mime = if bytes.starts_with(b"\x89PNG\r\n\x1a\n") { "image/png" }
        else if bytes.starts_with(b"\xff\xd8\xff") { "image/jpeg" }
        else if bytes.starts_with(b"GIF87a") || bytes.starts_with(b"GIF89a") { "image/gif" }
        else if bytes.starts_with(b"RIFF") && bytes.get(8..12) == Some(b"WEBP") { "image/webp" }
        else { return Err(ForgeError::new("markdown_image", "Supported local image formats: PNG, JPEG, GIF and WebP")); };
    Ok(format!("data:{mime};base64,{}", STANDARD.encode(bytes)))
}
fn safe_link(url: &str) -> bool {
    url.starts_with('#') || url.starts_with("https://") || url.starts_with("http://") || url.starts_with("mailto:")
}

pub fn render_markdown(source: &Path, options: &MarkdownOptions) -> ForgeResult<String> {
    let source = source.canonicalize().map_err(|e| ForgeError::new("source_missing", e))?;
    let folder = source.parent().ok_or_else(|| ForgeError::new("markdown_path", "Missing document folder"))?;
    let markdown = fs::read_to_string(&source).map_err(|e| ForgeError::new("markdown_read", e))?;
    if markdown.len() > 16 * 1024 * 1024 { return Err(ForgeError::new("markdown_size", "Markdown exceeds 16 MiB")); }
    let parser = Parser::new_ext(&markdown, Options::ENABLE_TABLES | Options::ENABLE_STRIKETHROUGH | Options::ENABLE_TASKLISTS | Options::ENABLE_FOOTNOTES | Options::ENABLE_MATH | Options::ENABLE_GFM);
    let mut events = Vec::new();
    let mut headings: Vec<(String, String)> = Vec::new();
    let mut heading: Option<(String, String, String)> = None;
    let mut mermaid: Option<String> = None;
    let mut embedded_bytes = 0usize;
    for event in parser {
        if let Some(code) = mermaid.as_mut() {
            match event {
                Event::End(TagEnd::CodeBlock) => {
                    let code = mermaid.take().unwrap();
                    events.push(Event::Html(format!("<pre class=\"mermaid\">{}</pre>", escape(&code)).into()));
                }
                Event::Text(text) => code.push_str(&text),
                _ => {}
            }
            continue;
        }
        match event {
            Event::Start(Tag::CodeBlock(CodeBlockKind::Fenced(info))) if info.trim() == "mermaid" => mermaid = Some(String::new()),
            Event::Start(Tag::Heading { level, .. }) => {
                let id = format!("heading-{}", headings.len() + 1);
                events.push(Event::Html(format!("<{level} id=\"{id}\">",).into()));
                heading = Some((id, String::new(), level.to_string()));
            }
            Event::End(TagEnd::Heading(_)) => {
                if let Some((id, title, level)) = heading.take() {
                    events.push(Event::Html(format!("</{level}>\n").into()));
                    headings.push((id, title));
                }
            }
            Event::Html(text) | Event::InlineHtml(text) => events.push(Event::Text(text)),
            Event::InlineMath(text) => events.push(Event::Html(format!("<span class=\"math-inline\">{}</span>", escape(&text)).into())),
            Event::DisplayMath(text) => events.push(Event::Html(format!("<div class=\"math-display\">{}</div>", escape(&text)).into())),
            Event::Start(Tag::Image { link_type, dest_url, title, id }) => {
                let url = image_url(folder, &dest_url)?;
                embedded_bytes += url.len();
                if embedded_bytes > 64 * 1024 * 1024 { return Err(ForgeError::new("markdown_size", "Embedded images exceed 64 MiB")); }
                events.push(Event::Start(Tag::Image { link_type, dest_url: CowStr::from(url), title, id }));
            }
            Event::Start(Tag::Link { link_type, dest_url, title, id }) => {
                let url = if safe_link(&dest_url) { dest_url } else { CowStr::from("#") };
                events.push(Event::Start(Tag::Link { link_type, dest_url: url, title, id }));
            }
            Event::Text(ref text) | Event::Code(ref text) => {
                if let Some((_, title, _)) = heading.as_mut() { title.push_str(text); }
                events.push(event);
            }
            _ => events.push(event)
        }
    }
    let mut body = String::new();
    html::push_html(&mut body, events.into_iter());
    let title = headings.first().map(|(_, title)| title.as_str()).unwrap_or("Markdown");
    let print_margins = format!("@page{{@top-center{{content:{};font:9pt 'Segoe UI'}}@bottom-left{{content:{};font:9pt 'Segoe UI'}}@bottom-right{{content:{};font:9pt 'Segoe UI'}}}}", css_string(&options.header), css_string(&options.footer), if options.page_numbers { "counter(page) \" / \" counter(pages)" } else { "\"\"" });
    let mut toc = String::new();
    if options.toc && !headings.is_empty() {
        toc.push_str("<nav aria-label=\"Contents\"><h2>目錄 / Contents</h2><ol>");
        for (id, title) in &headings { toc.push_str(&format!("<li><a href=\"#{id}\">{}</a></li>", escape(title))); }
        toc.push_str("</ol></nav>");
    }
    Ok(format!(r#"<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; font-src data:; connect-src 'none'; base-uri 'none'; form-action 'none'"><title>{}</title><style>@page{{size:A4;margin:18mm}}{print_margins}body{{font:11pt 'Segoe UI','Microsoft JhengHei',sans-serif;line-height:1.6;color:#17202a}}h1,h2,h3,h4{{break-after:avoid}}table{{border-collapse:collapse;width:100%;break-inside:auto}}th,td{{border:1px solid #bbb;padding:6px}}tr,img,pre{{break-inside:avoid}}img,svg{{max-width:100%}}pre{{white-space:pre-wrap;overflow-wrap:anywhere;background:#f3f4f6;padding:12px}}blockquote{{border-left:3px solid #bbb;padding-left:1em}}nav{{break-after:page}}a{{color:#1261a0}}code{{font-family:Consolas,monospace}}</style></head><body>{toc}{body}</body></html>"#, escape(title)))
}
