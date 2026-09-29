# PDF Forge 授權與第三方聲明

Copyright © 2026 yaosio232.

除另有標示外，PDF Forge 原始程式碼以 [GNU Affero General Public License version 3](LICENSE.md)（SPDX: `AGPL-3.0-only`）提供。這份授權允許使用、修改與再散布，也規定散布程式或提供網路互動服務時須保留授權告知並提供對應原始碼。完整權利與義務以授權全文為準。原始碼位於 <https://github.com/yaosio232/pdf-forge>；程式與原始碼均不提供保固。

本專案的授權不取代第三方套件的授權：

| 元件 | 版本 | 授權與聲明 |
|---|---|---|
| [iTextSharp](https://www.nuget.org/packages/iTextSharp/5.5.13.4) | 5.5.13.4 | iText Group NV；AGPLv3／商業授權雙軌。[原套件授權聲明](licenses/iTextSharp-5.5.13.4-LICENSE.md)含額外條款，例如保留 iText 製作或修改 PDF 時的 producer 資訊。 |
| [BouncyCastle.Cryptography](https://www.nuget.org/packages/BouncyCastle.Cryptography/2.4.0) | 2.4.0 | The Legion of the Bouncy Castle Inc.；MIT。[原套件授權聲明](licenses/BouncyCastle.Cryptography-2.4.0-LICENSE.md)。 |
| [Poppler](https://poppler.freedesktop.org/) | 26.07.0 | The Poppler Developers；GPL-2.0-or-later。可攜版 ZIP 使用 conda-forge 套件 `poppler=26.07.0=h6618ce5_3` 的 `pdftoppm.exe` 產生 Split Preview；[GPLv2 全文](licenses/Poppler-26.07.0-COPYING.txt)、原始碼、精確 recipe revision 與 binary hash 見 [Poppler 來源說明](licenses/Poppler-26.07.0-SOURCE.md)。 |
| .NET / Windows Forms | .NET 6 | 自包含 Windows 發佈會帶入 .NET 執行元件；其授權與第三方聲明由 [.NET 授權資訊](https://github.com/dotnet/core/blob/main/license-information.md)說明。 |

上述第三方聲明取自建置與可攜版封裝使用的套件，並不表示 PDF Forge 擁有這些套件的著作權。Poppler Windows bundle 的實際檔案、直接相依版本範圍與授權來源列在 [原生相依清單](licenses/Poppler-26.07.0-DEPENDENCIES.md)；各元件保留自己的授權。Microsoft Word 不隨 PDF Forge 提供；只有使用 Word 文件合併功能時才需要另外安裝桌面版 Word。
