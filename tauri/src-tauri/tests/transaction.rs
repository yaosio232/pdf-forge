use pdf_forge::transaction::{check_outputs, PreparedOutput};
use std::fs;

#[test]
fn rejects_source_alias_and_duplicate_outputs_before_writing() {
    let folder = tempfile::tempdir().unwrap();
    let source = folder.path().join("source.pdf");
    fs::write(&source, b"original").unwrap();
    assert!(check_outputs(&[source.clone()], &[source.clone()], true).is_err());
    let output = folder.path().join("new.pdf");
    assert!(check_outputs(&[source], &[output.clone(),output], false).is_err());
}
#[test]
fn abandoned_transaction_preserves_existing_output() {
    let folder = tempfile::tempdir().unwrap();
    let output = folder.path().join("existing.pdf");
    fs::write(&output, b"existing").unwrap();
    let temporary;
    {
        let transaction = PreparedOutput::new(&output, true).unwrap();
        temporary = transaction.path().to_path_buf();
        fs::write(transaction.path(), b"partial").unwrap();
    }
    assert_eq!(fs::read(&output).unwrap(), b"existing");
    assert!(!temporary.exists());
}
#[test]
fn commit_requires_explicit_overwrite_and_replaces_atomically() {
    let folder = tempfile::tempdir().unwrap();
    let output = folder.path().join("existing.pdf");
    fs::write(&output, b"existing").unwrap();
    assert!(PreparedOutput::new(&output, false).is_err());
    let transaction = PreparedOutput::new(&output, true).unwrap();
    fs::write(transaction.path(), b"validated").unwrap();
    transaction.commit().unwrap();
    assert_eq!(fs::read(output).unwrap(), b"validated");
}
