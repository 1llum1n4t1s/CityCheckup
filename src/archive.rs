use std::io::{self, Read};
use zip::ZipArchive;

/// ZIP の中で、名前が `suffix` で終わる最初の項目の名前を返す。
///
/// zip 9 からは `file_names()` が名前ごとに `Result` を返す（名前の符号化が不正な項目があり得るため）。
/// 読めない名前の項目は探している項目ではないので飛ばし、従来どおり最初に一致した名前を使う。
pub fn find_entry_name<R: Read + io::Seek>(zip: &ZipArchive<R>, suffix: &str) -> Option<String> {
    zip.file_names()
        .filter_map(Result::ok)
        .find(|name| name.ends_with(suffix))
        .map(|name| name.into_owned())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs::File;

    #[test]
    fn real_saves_show_entity_counts_and_references() {
        for (var, counts) in [
            ("COK_TEST_SAMPLE", [17601, 15154, 17839, 73445]),
            ("COK_TEST_MICHATOWN", [107856, 95402, 57651, 673817]),
        ] {
            let Some(path) = std::env::var_os(var) else {
                continue;
            };
            let mut zip = ZipArchive::new(File::open(&path).unwrap()).unwrap();
            let name = find_entry_name(&zip, ".SaveGameData").unwrap();
            let entry = zip.by_name(&name).unwrap();
            let report = crate::save_data::analyze(entry).unwrap().render();
            for (name, count) in ["車両", "駐車車両", "歩行者の実体", "市民データ"]
                .iter()
                .zip(counts)
            {
                assert!(
                    report
                        .lines()
                        .any(|line| line.starts_with(&format!("{name}: {count}  ")))
                );
            }
            if var == "COK_TEST_SAMPLE" {
                assert!(report.contains("CurrentVehicle の参照\r\n  車両: 7104  人物の実体: 0"));
                assert!(report.contains("CurrentTransport の参照\r\n  車両: 0  人物の実体: 17837"));
                assert!(report.contains("38735 → 684995"));
            } else {
                assert!(report.contains("CurrentVehicle の参照\r\n  車両: 18688  人物の実体: 0"));
                assert!(report.contains("CurrentTransport の参照\r\n  車両: 0  人物の実体: 57645"));
                assert!(report.contains("Owner の参照\r\n  車両: 5277  人物の実体: 1"));
                assert!(report.contains("6410643 → 3879730"));
            }
            assert!(report.contains("元ID → 先ID"));
        }
    }
}
