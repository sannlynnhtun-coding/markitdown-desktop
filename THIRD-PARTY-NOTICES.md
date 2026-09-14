# Third-party notices

MarkItDown Desktop is an independent community application. It is not an official Microsoft product. It is powered by the Microsoft MarkItDown open-source project and does not use Microsoft branding or logos.

The distributed application includes or uses the following major third-party components. Their respective licenses apply to those components.

| Component | Version | License / notice |
| --- | --- | --- |
| Microsoft MarkItDown | 0.1.7 | MIT; bundled license in `ThirdPartyNotices/MARKITDOWN-LICENSE.txt` |
| Python (embedded x64) | 3.13.13 | Python Software Foundation License; bundled license in `ThirdPartyNotices/PYTHON-LICENSE.txt` |
| ExifTool | 13.59 | Artistic License / GPL option; bundled license in `ThirdPartyNotices/EXIFTOOL-LICENSE.txt` |
| Uno Platform | 6.7.22 | Apache-2.0 |
| .NET / Windows App SDK | 10 / package-pinned | MIT and applicable Microsoft terms |
| Microsoft Edge WebView2 Runtime | Evergreen offline installer | Microsoft Edge WebView2 Runtime terms |
| Markdig | 1.3.2 | BSD-2-Clause |
| CommunityToolkit.Mvvm | package-pinned | MIT |
| WiX Toolset | 7.0.0 | Microsoft Reciprocal License plus Open Source Maintenance Fee terms accepted by the project owner |

The exact hash-locked Python dependency inventory is distributed as `ThirdPartyNotices/PYTHON-PACKAGES.txt`; package license files remain alongside their packages in the bundled Python runtime. NuGet dependency versions are centrally pinned in `Directory.Packages.props`, with the major runtime components and their license families listed above.

Source links:

- https://github.com/microsoft/markitdown
- https://www.python.org/
- https://exiftool.org/
- https://platform.uno/
- https://github.com/xoofx/markdig
- https://github.com/wixtoolset/wix
- https://developer.microsoft.com/microsoft-edge/webview2/
