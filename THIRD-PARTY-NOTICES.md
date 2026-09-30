# 第三方组件与许可声明 (Third-Party Notices)

本文件列出 ImgsToPDF 项目使用并随发行版一起分发的第三方组件及其许可条款。
本项目自身以 MIT 许可证发布（见 [LICENSE.txt](LICENSE.txt)）；下列组件各自遵循其原始许可。

分发包中与第三方相关的文件位于 `ImgsToPDF/Core/`（由 `ImgsToPDFCore` 项目构建时铺出）：

| 组件 | 许可证 | 版权所有者 | 分发文件 |
| --- | --- | --- | --- |
| [iTextSharp](https://itextpdf.com/) | AGPL-3.0 或商业许可 | Copyright (c) 1998-2026 iText Group NV | `itextsharp.dll` |
| [BouncyCastle.Cryptography](https://www.bouncycastle.org/) | MIT | Copyright © Legion of the Bouncy Castle Inc. 2000-2026 | `BouncyCastle.Cryptography.dll` |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | MIT | Copyright (c) 2025 Adam Hathcock | `SharpCompress.dll` |
| [CommandLineParser](https://github.com/commandlineparser/commandline) | MIT | Copyright (c) 2005 - 2015 Giacomo Stelluti Scala & Contributors | `CommandLine.dll` |
| [XLua](https://github.com/Tencent/xLua) | MIT | Copyright (c) 2016-2018 Tencent | `xlua.dll`、`XLua.Mini.dll` |
| [Lua](https://www.lua.org/)（嵌入在 `xlua.dll` 中） | MIT | Copyright © 1994–2025 Lua.org, PUC-Rio | `xlua.dll` |
| [libwebp](https://chromium.googlesource.com/webm/libwebp) | BSD-3-Clause | Copyright (c) 2010, Google Inc. | `libwebp_x64.dll` |
| [WebP-wrapper](https://github.com/JosePineiro/WebP-wrapper)（源码内嵌） | MIT | Jose M. Piñeiro | `ImgsToPDFCore/WebPWrapper.cs` |
| Microsoft .NET 支持库<br> | MIT | © Microsoft Corporation | `Microsoft.Bcl.AsyncInterfaces.dll`、`System.Buffers.dll`、`System.Memory.dll`、`System.Numerics.Vectors.dll`、`System.Runtime.CompilerServices.Unsafe.dll`、`System.Text.Encoding.CodePages.dll`、`System.Threading.Tasks.Extensions.dll`（`System.ValueTuple` 由 .NET Framework 4.8 自带，不单独分发） |

`.NET Framework 4.8` 运行时本身由微软提供、不随本项目分发，遵循其自身的许可条款。

---

## iTextSharp 与 AGPL

iTextSharp 采用双许可：AGPL-3.0 或商业许可。本项目使用的是 AGPL-3.0 分支。

AGPL-3.0 全文见 <https://www.gnu.org/licenses/agpl-3.0.html>，iText 随包提供副本 `gnu-agpl-v3.0.md`。

---

## MIT 许可证全文

以下组件均以 MIT 许可证授权，各自保留其版权行：

- BouncyCastle.Cryptography — Copyright © Legion of the Bouncy Castle Inc. 2000-2026
- SharpCompress — Copyright (c) 2025 Adam Hathcock
- CommandLineParser — Copyright (c) 2005 - 2015 Giacomo Stelluti Scala & Contributors
- XLua — Copyright (c) 2016-2018 Tencent
- Lua 5.4.8 — Copyright © 1994–2025 Lua.org, PUC-Rio
- WebP-wrapper — Jose M. Piñeiro
- Microsoft .NET 支持库 — © Microsoft Corporation. All rights reserved.

```text
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## libwebp（BSD-3-Clause）许可证全文

`libwebp_x64.dll` 为 Google 的 WebP 图像编解码库 1.2.4 版二进制。

```text
Copyright (c) 2010, Google Inc. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

  * Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.

  * Redistributions in binary form must reproduce the above copyright
    notice, this list of conditions and the following disclaimer in the
    documentation and/or other materials provided with the distribution.

  * Neither the name of Google nor the names of its contributors may be
    used to endorse or promote products derived from this software
    without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.
```
