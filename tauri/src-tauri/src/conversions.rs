//! These operations must run in the supervisor's disposable worker, never the UI process.
use crate::model::{ForgeError, ForgeResult, MarkdownOptions};
use std::path::Path;

pub fn validate_docx(path: &Path) -> ForgeResult<()> {
    use std::io::Read;
    let file = std::fs::File::open(path).map_err(|e| ForgeError::new("docx_invalid", e))?;
    let mut archive = zip::ZipArchive::new(file).map_err(|e| ForgeError::new("docx_invalid", e))?;
    for (name, root) in [("[Content_Types].xml", "Types"), ("_rels/.rels", "Relationships"), ("word/document.xml", "document")] {
        let mut entry = archive.by_name(name).map_err(|e| ForgeError::new("docx_invalid", e))?;
        if entry.size() == 0 || entry.size() > 64 * 1024 * 1024 { return Err(ForgeError::new("docx_invalid", "DOCX XML entry has invalid size")); }
        let mut text = String::new();
        (&mut entry).take(64 * 1024 * 1024 + 1).read_to_string(&mut text).map_err(|e| ForgeError::new("docx_invalid", e))?;
        if text.len() > 64 * 1024 * 1024 { return Err(ForgeError::new("docx_invalid", "DOCX XML decompression limit exceeded")); }
        let xml = roxmltree::Document::parse(&text).map_err(|e| ForgeError::new("docx_invalid", e))?;
        let namespace = xml.root_element().tag_name().namespace().unwrap_or("");
        let valid_namespace = match root {
            "Types" => namespace == "http://schemas.openxmlformats.org/package/2006/content-types",
            "Relationships" => namespace == "http://schemas.openxmlformats.org/package/2006/relationships",
            _ => matches!(namespace, "http://schemas.openxmlformats.org/wordprocessingml/2006/main" | "http://purl.oclc.org/ooxml/wordprocessingml/main"),
        };
        if xml.root_element().tag_name().name() != root || !valid_namespace {
            return Err(ForgeError::new("docx_invalid", "DOCX package has invalid XML"));
        }
        let valid_content = match root {
            "Types" => xml.root_element().children().any(|n| n.has_tag_name((namespace, "Override"))
                && n.attribute("PartName") == Some("/word/document.xml")
                && n.attribute("ContentType") == Some("application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
            "Relationships" => xml.root_element().children().any(|n| n.has_tag_name((namespace, "Relationship"))
                && n.attribute("Id").is_some_and(|id| !id.is_empty())
                && matches!(n.attribute("Type"), Some("http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" | "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument"))
                && matches!(n.attribute("Target"), Some("word/document.xml" | "/word/document.xml"))
                && n.attribute("TargetMode").unwrap_or("Internal") == "Internal"),
            _ => xml.root_element().children().any(|n| n.has_tag_name((namespace, "body"))),
        };
        if !valid_content { return Err(ForgeError::new("docx_invalid", "DOCX package does not declare an internal Word document")); }
    }
    Ok(())
}

pub fn word_to_pdf(source: &Path, output: &Path) -> ForgeResult<()> { word_convert(source, output, true) }
pub fn pdf_to_word(source: &Path, output: &Path) -> ForgeResult<()> { word_convert(source, output, false) }

#[cfg(not(windows))]
fn word_convert(_: &Path, _: &Path, _: bool) -> ForgeResult<()> { Err(ForgeError::new("word_unavailable", "Microsoft Word conversion requires Windows and desktop Word")) }
#[cfg(not(windows))]
pub fn markdown_to_pdf(_: &Path, _: &Path, _: &MarkdownOptions) -> ForgeResult<()> { Err(ForgeError::new("webview_unavailable", "Markdown PDF printing requires Windows WebView2")) }

#[cfg(windows)]
fn word_convert(source: &Path, output: &Path, pdf: bool) -> ForgeResult<()> {
    win::word(source, output, pdf).map_err(|e| ForgeError::new("word_conversion", e))
}
#[cfg(windows)]
pub fn markdown_to_pdf(source: &Path, output: &Path, options: &MarkdownOptions) -> ForgeResult<()> {
    let html = crate::markdown::render_markdown(source, options)?;
    win::print(&html, output, options).map_err(|e| {
        let detail=e.to_string();
        let code = if detail == "Conversion cancelled" { "CANCELLED" }
            else if detail == "Markdown renderer: error:MARKDOWN_MATH" { "MARKDOWN_MATH" }
            else if detail == "Markdown renderer: error:MARKDOWN_MERMAID" { "MARKDOWN_MERMAID" }
            else if detail == "Markdown renderer: error:MARKDOWN_IMAGE_RENDER" { "MARKDOWN_IMAGE_RENDER" }
            else { "markdown_print" };
        ForgeError::new(code, e)
    })
}

#[cfg(windows)]
mod win {
    use super::*;
    use std::{fs, path::PathBuf, sync::mpsc, time::{SystemTime, UNIX_EPOCH}};
    use windows::{core::{w, BSTR, GUID, Interface, PCWSTR, PWSTR}, Win32::{Foundation::{CloseHandle, FILETIME, HWND, LPARAM, LRESULT, RECT, WPARAM}, System::{Com::*, Threading::{GetProcessTimes, OpenProcess, PROCESS_QUERY_LIMITED_INFORMATION}, Variant::VARIANT}, UI::WindowsAndMessaging::*}};
    use webview2_com::{Microsoft::Web::WebView2::Win32::*, *};
    type Result<T> = std::result::Result<T, Box<dyn std::error::Error>>;
    struct Apartment;
    impl Apartment {
        fn new() -> Result<Self> { unsafe { CoInitializeEx(None, COINIT_APARTMENTTHREADED).ok()?; } Ok(Self) }
    }
    impl Drop for Apartment { fn drop(&mut self) { unsafe { CoUninitialize(); } } }
    fn wide(s: &str) -> Vec<u16> { s.encode_utf16().chain(Some(0)).collect() }
    fn office_path(path: &Path) -> String {
        let value = path.to_string_lossy();
        if let Some(tail) = value.strip_prefix(r"\\?\UNC\") { format!(r"\\{tail}") }
        else { value.strip_prefix(r"\\?\").unwrap_or(&value).to_string() }
    }
    fn pump<T>(rx: mpsc::Receiver<T>) -> Result<T> {
        loop {
            match rx.try_recv() {
                Ok(value) => return Ok(value),
                Err(mpsc::TryRecvError::Disconnected) => return Err("WebView callback disconnected".into()),
                Err(mpsc::TryRecvError::Empty) => {}
            }
            if std::env::var_os("PDF_FORGE_CANCEL_FILE").is_some_and(|p| Path::new(&p).exists()) { return Err("Conversion cancelled".into()); }
            let mut message = MSG::default();
            unsafe {
                while PeekMessageW(&mut message, None, 0, 0, PM_REMOVE).as_bool() {
                    if message.message == WM_QUIT { return Err("Print worker window closed".into()); }
                    let _ = TranslateMessage(&message);
                    DispatchMessageW(&message);
                }
            }
            std::thread::sleep(std::time::Duration::from_millis(10));
        }
    }
    fn call(object: &IDispatch, name: &str, flags: DISPATCH_FLAGS, mut args: Vec<VARIANT>) -> windows::core::Result<VARIANT> {
        let name = wide(name);
        let mut id = 0;
        let mut named = -3i32; // DISPID_PROPERTYPUT
        args.reverse(); // COM positional arguments are in reverse order.
        let params = DISPPARAMS {
            rgvarg: args.as_mut_ptr(), cArgs: args.len() as u32,
            rgdispidNamedArgs: if flags == DISPATCH_PROPERTYPUT { &mut named } else { std::ptr::null_mut() },
            cNamedArgs: if flags == DISPATCH_PROPERTYPUT { 1 } else { 0 },
        };
        let mut result = VARIANT::default();
        let mut exception = EXCEPINFO::default();
        unsafe {
            object.GetIDsOfNames(&GUID::zeroed(), &PCWSTR(name.as_ptr()), 1, 0x0409, &mut id)?;
            object.Invoke(id, &GUID::zeroed(), 0x0409, flags, &params, Some(&mut result), Some(&mut exception), None)?;
        }
        Ok(result)
    }
    fn put(object: &IDispatch, name: &str, value: impl Into<VARIANT>) -> windows::core::Result<()> {
        call(object, name, DISPATCH_PROPERTYPUT, vec![value.into()]).map(|_| ())
    }
    fn object(parent: &IDispatch, name: &str) -> windows::core::Result<IDispatch> {
        IDispatch::try_from(&call(parent, name, DISPATCH_PROPERTYGET, vec![])?)
    }
    struct Word { app: IDispatch, owned: bool, options: Option<(IDispatch, Vec<(&'static str, VARIANT)>)> }
    impl Drop for Word {
        fn drop(&mut self) {
            if self.owned {
                if let Some((options, values)) = self.options.take() {
                    for (name, value) in values { let _ = put(&options, name, value); }
                }
                let _ = call(&self.app, "Quit", DISPATCH_METHOD, vec![0i32.into()]);
            }
        }
    }
    struct Document(IDispatch);
    impl Drop for Document {
        fn drop(&mut self) { let _ = call(&self.0, "Close", DISPATCH_METHOD, vec![0i32.into()]); }
    }
    fn ticks(time: FILETIME) -> u64 { ((time.dwHighDateTime as u64) << 32) | time.dwLowDateTime as u64 }
    pub fn word(source: &Path, output: &Path, pdf: bool) -> Result<()> {
        let source = source.canonicalize()?;
        if output.exists() { return Err("Worker output must be a new staging file".into()); }
        let output = output.parent().ok_or("Missing output directory")?.canonicalize()?.join(output.file_name().ok_or("Missing output name")?);
        if source == output { return Err("Output equals source".into()); }
        let _com = Apartment::new()?;
        let started = SystemTime::now().duration_since(UNIX_EPOCH)?.as_nanos() / 100 + 116444736000000000;
        let clsid = unsafe { CLSIDFromProgID(w!("Word.Application"))? };
        let app: IDispatch = unsafe { CoCreateInstance(&clsid, None, CLSCTX_LOCAL_SERVER)? };
        // Never quit or change a pre-existing user's Word instance.
        let mut word = Word { app, owned: false, options: None };
        let hwnd = i32::try_from(&call(&word.app, "Hwnd", DISPATCH_PROPERTYGET, vec![])?)?;
        let mut pid = 0;
        unsafe { GetWindowThreadProcessId(HWND(hwnd as isize as *mut _), Some(&mut pid)); }
        if pid == 0 { return Err("Cannot determine Word process identity".into()); }
        let process = unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid)? };
        let mut creation = FILETIME::default();
        let mut exit = FILETIME::default();
        let mut kernel = FILETIME::default();
        let mut user = FILETIME::default();
        let times = unsafe { GetProcessTimes(process, &mut creation, &mut exit, &mut kernel, &mut user) };
        unsafe { let _ = CloseHandle(process); }
        times?;
        if (ticks(creation) as u128) < started { return Err("Word reused an existing process; conversion refused to protect the user's session".into()); }
        word.owned = true;
        let identity = std::env::var_os("PDF_FORGE_WORD_IDENTITY").ok_or("Word worker requires a supervisor identity file")?;
        fs::write(identity, serde_json::to_vec(&serde_json::json!({"pid":pid,"creationTime":ticks(creation)}))?)?;
        put(&word.app, "Visible", false)?;
        put(&word.app, "DisplayAlerts", 0i32)?;
        put(&word.app, "AutomationSecurity", 3i32)?; // msoAutomationSecurityForceDisable
        let options = object(&word.app, "Options")?;
        let mut saved = Vec::new();
        for name in ["UpdateLinksAtOpen", "UpdateFieldsAtPrint", "ConfirmConversions"] {
            saved.push((name, call(&options, name, DISPATCH_PROPERTYGET, vec![])?));
        }
        word.options = Some((options.clone(), saved));
        for name in ["UpdateLinksAtOpen", "UpdateFieldsAtPrint", "ConfirmConversions"] { put(&options, name, false)?; }
        let docs = object(&word.app, "Documents")?;
        // Filename, ConfirmConversions, ReadOnly, AddToRecentFiles.
        let document = Document(IDispatch::try_from(&call(&docs, "Open", DISPATCH_METHOD, vec![
            BSTR::from(office_path(&source).as_str()).into(), false.into(), true.into(), false.into(),
            BSTR::from("PDF-Forge-does-not-open-password-protected-Word-documents").into(),
            BSTR::from("PDF-Forge-does-not-open-password-protected-Word-templates").into()
        ])?)?);
        if pdf {
            // Print quality, complete document, no properties/tags, and heading bookmarks.
            call(&document.0, "ExportAsFixedFormat", DISPATCH_METHOD, vec![
                BSTR::from(office_path(&output).as_str()).into(), 17i32.into(), false.into(),
                0i32.into(), 0i32.into(), 1i32.into(), 1i32.into(), 0i32.into(),
                false.into(), false.into(), 1i32.into(), false.into(), true.into(), false.into()
            ])?;
        } else {
            call(&document.0, "SaveAs2", DISPATCH_METHOD, vec![BSTR::from(office_path(&output).as_str()).into(), 16i32.into()])?;
        }
        if fs::metadata(&output)?.len() == 0 { return Err("Word produced an empty file".into()); }
        Ok(())
    }

    struct HiddenWindow(HWND);
    unsafe extern "system" fn window_proc(hwnd: HWND, message: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
        DefWindowProcW(hwnd, message, wparam, lparam)
    }
    impl Drop for HiddenWindow { fn drop(&mut self) { unsafe { let _ = DestroyWindow(self.0); } } }
    struct Controller(ICoreWebView2Controller);
    impl Drop for Controller { fn drop(&mut self) { unsafe { let _ = self.0.Close(); } } }
    fn assets() -> Result<PathBuf> {
        if let Some(path) = std::env::var_os("PDF_FORGE_MARKDOWN_ASSETS") { return Ok(PathBuf::from(path)); }
        Ok(std::env::current_exe()?.parent().ok_or("Missing executable folder")?.join("resources/markdown"))
    }
    pub fn print(html: &str, output: &Path, options: &MarkdownOptions) -> Result<()> {
        if output.exists() { return Err("Worker output must be a new staging file".into()); }
        let _com = Apartment::new()?;
        let class = WNDCLASSW { lpfnWndProc: Some(window_proc), lpszClassName: w!("PdfForgeOfflinePrint"), ..Default::default() };
        unsafe { RegisterClassW(&class); }
        let window = HiddenWindow(unsafe { CreateWindowExW(Default::default(), w!("PdfForgeOfflinePrint"), w!(""), WS_OVERLAPPED, 0, 0, 800, 1100, None, None, None, None)? });
        let worker_directory = std::env::var_os("PDF_FORGE_CANCEL_FILE").and_then(|p| PathBuf::from(p).parent().map(Path::to_path_buf));
        let profile = tempfile::tempdir_in(worker_directory.as_deref().unwrap_or(output.parent().ok_or("Missing output directory")?))?;
        let profile_path = wide(profile.path().to_string_lossy().as_ref());
        let environment_options: ICoreWebView2EnvironmentOptions = CoreWebView2EnvironmentOptions::default().into();
        unsafe { environment_options.SetAdditionalBrowserArguments(w!("--disable-background-networking --disable-component-update --disable-sync --no-first-run --disable-features=AutofillServerCommunication"))?; }
        let (tx, rx) = mpsc::channel();
        let handler = CreateCoreWebView2EnvironmentCompletedHandler::create(Box::new(move |status, environment| {
            let _ = tx.send(status.map(|_| environment)); Ok(())
        }));
        unsafe { CreateCoreWebView2EnvironmentWithOptions(PCWSTR::null(), PCWSTR(profile_path.as_ptr()), &environment_options, &handler)?; }
        let environment = pump(rx)??.ok_or("WebView2 Runtime is not installed")?;
        if options.page_numbers || !options.header.is_empty() || !options.footer.is_empty() {
            let mut version = PWSTR::null();
            unsafe { environment.BrowserVersionString(&mut version)?; }
            let version = CoTaskMemPWSTR::from(version).to_string();
            if version.split('.').next().and_then(|v| v.parse::<u32>().ok()).unwrap_or(0) < 131 {
                return Err("Headers, footers and page numbers require WebView2 Runtime 131 or newer".into());
            }
        }
        let (tx, rx) = mpsc::channel();
        let hwnd = window.0;
        let env = environment.clone();
        let handler = CreateCoreWebView2ControllerCompletedHandler::create(Box::new(move |status, controller| {
            let _ = tx.send(status.map(|_| controller)); Ok(())
        }));
        unsafe { env.CreateCoreWebView2Controller(hwnd, &handler)?; }
        let controller = Controller(pump(rx)??.ok_or("Cannot create isolated WebView2")?);
        let env6: ICoreWebView2Environment6 = environment.cast()?;
        let view = unsafe {
            controller.0.SetBounds(RECT {left:0,top:0,right:800,bottom:1100})?;
            controller.0.SetIsVisible(false)?;
            controller.0.CoreWebView2()?
        };
        unsafe {
            let settings = view.Settings()?;
            settings.SetAreDevToolsEnabled(false)?;
            settings.SetAreDefaultContextMenusEnabled(false)?;
            settings.SetAreDefaultScriptDialogsEnabled(false)?;
            settings.SetAreHostObjectsAllowed(false)?;
        }
        let document_path = profile.path().join("document.html");
        let document_url = format!("file:///{}", document_path.to_string_lossy().replace('\\', "/").bytes().map(|b| {
            if b.is_ascii_alphanumeric() || b"/:._-~".contains(&b) { (b as char).to_string() } else { format!("%{b:02X}") }
        }).collect::<String>());
        // Only the generated main document and inline raster images/fonts may be read.
        let mut request_token = 0;
        let permitted_url = document_url.clone();
        unsafe {
            view.AddWebResourceRequestedFilter(w!("*"), COREWEBVIEW2_WEB_RESOURCE_CONTEXT_ALL)?;
            view.add_WebResourceRequested(&WebResourceRequestedEventHandler::create(Box::new(move |_, args| {
                if let Some(args) = args {
                    let request = args.Request()?;
                    let mut uri = PWSTR::null();
                    request.Uri(&mut uri)?;
                    let uri = CoTaskMemPWSTR::from(uri).to_string();
                    let mut context = COREWEBVIEW2_WEB_RESOURCE_CONTEXT_ALL;
                    args.ResourceContext(&mut context)?;
                    if uri == permitted_url && context == COREWEBVIEW2_WEB_RESOURCE_CONTEXT_DOCUMENT { return Ok(()); }
                    if context == COREWEBVIEW2_WEB_RESOURCE_CONTEXT_IMAGE && ["data:image/png;base64,", "data:image/jpeg;base64,", "data:image/gif;base64,", "data:image/webp;base64,"].iter().any(|prefix| uri.starts_with(prefix)) { return Ok(()); }
                    if context == COREWEBVIEW2_WEB_RESOURCE_CONTEXT_FONT && ["data:font/woff2;base64,", "data:font/woff;base64,", "data:font/ttf;base64,"].iter().any(|prefix| uri.starts_with(prefix)) { return Ok(()); }
                    let response = environment.CreateWebResourceResponse(None, 403, w!("Offline only"), w!("Content-Type: text/plain"))?;
                    args.SetResponse(&response)?;
                }
                Ok(())
            })), &mut request_token)?;
        }
        let (tx, rx) = mpsc::channel();
        let mut message_token = 0;
        unsafe {
            view.add_WebMessageReceived(&WebMessageReceivedEventHandler::create(Box::new(move |_, args| {
                if let Some(args) = args {
                    let mut text = PWSTR::null();
                    args.TryGetWebMessageAsString(&mut text)?;
                    let value = CoTaskMemPWSTR::from(text).to_string();
                    let _ = tx.send(value);
                }
                Ok(())
            })), &mut message_token)?;
        }
        let asset_dir = assets()?;
        let js = fs::read_to_string(asset_dir.join("renderer.js"))?;
        let css = fs::read_to_string(asset_dir.join("renderer.css"))?;
        // Trusted build assets are embedded, never interpolated from document content.
        let readiness = r#"<script>(async()=>{try{await window.renderMarkdown();await document.fonts.ready;await Promise.all([...document.images].map(i=>i.complete?(i.naturalWidth?Promise.resolve():Promise.reject(Error('Image decode failed'))):new Promise((r,j)=>{i.onload=r;i.onerror=()=>j(Error('Image decode failed'))})));window.chrome.webview.postMessage('ready')}catch(e){window.chrome.webview.postMessage('error:'+String(e))}})()</script>"#;
        let html = html.replace("</head>", &format!("<style>{}</style><script>{}</script></head>", css.replace("</style", "<\\/style"), js.replace("</script", "<\\/script"))).replace("</body>", &format!("{readiness}</body>"));
        fs::write(&document_path, html)?;
        let url = wide(&document_url);
        unsafe { view.Navigate(PCWSTR(url.as_ptr()))?; }
        let state = pump(rx)?;
        if state != "ready" { return Err(format!("Markdown renderer: {state}").into()); }
        let settings = unsafe { env6.CreatePrintSettings()? };
        unsafe {
            settings.SetPageWidth(8.2677165354)?;
            settings.SetPageHeight(11.6929133858)?;
            settings.SetMarginTop(0.7086614173)?;
            settings.SetMarginBottom(0.7086614173)?;
            settings.SetMarginLeft(0.7086614173)?;
            settings.SetMarginRight(0.7086614173)?;
            settings.SetShouldPrintBackgrounds(true)?;
            // CSS page margin boxes provide independently configurable header/footer/page numbers.
            settings.SetShouldPrintHeaderAndFooter(false)?;
        }
        let view7: ICoreWebView2_7 = view.cast()?;
        let output_path = wide(output.to_string_lossy().as_ref());
        let (tx, rx) = mpsc::channel();
        let handler = PrintToPdfCompletedHandler::create(Box::new(move |status, success| {
            let _ = tx.send(status.map(|_| success)); Ok(())
        }));
        unsafe { view7.PrintToPdf(PCWSTR(output_path.as_ptr()), &settings, &handler)?; }
        if !pump(rx)?? { return Err("WebView2 PrintToPdf failed".into()); }
        if fs::metadata(output)?.len() == 0 { return Err("WebView2 produced an empty PDF".into()); }
        Ok(())
    }
}
