use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ForgeError {
    pub code: String,
    pub detail: String,
}
impl ForgeError {
    pub fn new(code: &str, detail: impl ToString) -> Self { Self { code: code.into(), detail: detail.to_string() } }
}
pub type ForgeResult<T> = Result<T, ForgeError>;

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct MarkdownOptions {
    pub toc: bool,
    pub header: String,
    pub footer: String,
    pub page_numbers: bool,
}
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SplitRange { pub first: u32, pub last: u32, pub name: String }
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase")]
pub enum JobRequest {
    Split { source: String, directory: String, ranges: Vec<SplitRange>, overwrite: bool },
    Merge { sources: Vec<String>, output: String, overwrite: bool },
    Unlock { sources: Vec<String>, password: String, directory: String },
    PdfToWord { source: String, output: String, overwrite: bool },
    Markdown { sources: Vec<String>, output: String, merged: bool, overwrite: bool, options: MarkdownOptions },
}
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ItemResult {
    pub source: String,
    pub output: Option<String>,
    pub pages: Option<u32>,
    pub error: Option<ForgeError>,
}
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct JobEvent {
    pub job_id: String,
    pub status: String,
    pub phase: String,
    pub completed: usize,
    pub total: usize,
    pub items: Vec<ItemResult>,
    pub error: Option<ForgeError>,
}
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PdfInfo { pub pages: u32, pub encrypted: bool }
