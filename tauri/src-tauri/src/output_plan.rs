//! Output locations are derived from selected sources; the UI never chooses output folders.
use crate::model::*;
use std::path::Path;

pub fn automatic_output(mut request:JobRequest)->ForgeResult<JobRequest>{
    let source=match &request{
        JobRequest::Split{source,..}|JobRequest::PdfToWord{source,..}=>source,
        JobRequest::Merge{sources,..}|JobRequest::Unlock{sources,..}|JobRequest::Markdown{sources,..}=>sources.first().ok_or_else(||ForgeError::new("NO_FILES","Choose input files"))?,
    };
    let path=Path::new(source).canonicalize().map_err(|e|ForgeError::new("SOURCE_MISSING",e))?;
    let folder=path.parent().ok_or_else(||ForgeError::new("PATH","Source folder unavailable"))?;
    let stem=path.file_stem().unwrap_or_default().to_string_lossy();
    let directory=folder.to_string_lossy().into_owned();
    let merged=folder.join(format!("{stem}_merged.pdf")).to_string_lossy().into_owned();
    let word=folder.join(format!("{stem}.docx")).to_string_lossy().into_owned();
    match &mut request{
        JobRequest::Split{directory:output,..}|JobRequest::Unlock{directory:output,..}=>*output=directory,
        JobRequest::Merge{output,..}=>*output=merged,
        JobRequest::PdfToWord{output,..}=>*output=word,
        JobRequest::Markdown{output,merged:is_merged,..}=>*output=if *is_merged{merged}else{directory},
    }
    Ok(request)
}

#[cfg(test)]mod tests{
    use super::*;
    #[test]fn selected_source_determines_outputs_and_ignores_client_path(){
        let directory=tempfile::tempdir().unwrap();let source=directory.path().join("document.pdf");std::fs::write(&source,b"synthetic").unwrap();
        let source=source.to_string_lossy().into_owned();let root=directory.path().canonicalize().unwrap();
        for (request,expected) in [
            (JobRequest::Merge{sources:vec![source.clone()],output:"unselected/output.pdf".into(),overwrite:false},root.join("document_merged.pdf")),
            (JobRequest::PdfToWord{source:source.clone(),output:"".into(),overwrite:false},root.join("document.docx")),
            (JobRequest::Markdown{sources:vec![source],output:"".into(),merged:false,overwrite:false,options:MarkdownOptions::default()},root.clone()),
        ]{
            let request=automatic_output(request).unwrap();
            let output=match request{JobRequest::Merge{output,..}|JobRequest::PdfToWord{output,..}|JobRequest::Markdown{output,..}=>output,_=>unreachable!()};
            assert_eq!(Path::new(&output),expected);
        }
    }
    #[test]fn split_keeps_range_names_and_unlock_uses_source_folder(){
        let directory=tempfile::tempdir().unwrap();let source=directory.path().join("document.pdf");std::fs::write(&source,b"synthetic").unwrap();let source=source.to_string_lossy().into_owned();
        let request=automatic_output(JobRequest::Split{source:source.clone(),directory:"".into(),ranges:vec![SplitRange{first:1,last:7,name:"chosen.pdf".into()}],overwrite:false}).unwrap();
        if let JobRequest::Split{directory:output,ranges,..}=request{assert_eq!(Path::new(&output),directory.path().canonicalize().unwrap());assert_eq!(ranges[0].name,"chosen.pdf");}else{panic!("Wrong operation");}
        let request=automatic_output(JobRequest::Unlock{sources:vec![source],password:"known".into(),directory:"".into()}).unwrap();
        if let JobRequest::Unlock{directory:output,..}=request{assert_eq!(Path::new(&output),directory.path().canonicalize().unwrap());}else{panic!("Wrong operation");}
    }
}
