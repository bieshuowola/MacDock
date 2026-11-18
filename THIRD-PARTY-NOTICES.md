# 平台组件与素材来源

MacDock 1.7.1 随程序附带 Microsoft.Windows.SDK.NET.dll 和 WinRT.Runtime.dll，来自固定的 Microsoft.Windows.SDK.NET.Ref 10.0.19041.56 targeting pack。Windows Composition 和 DWM 实现在操作系统内，未复制系统 DLL。

- [Windows SDK 包与许可入口](https://www.nuget.org/packages/Microsoft.Windows.SDK.NET.Ref/10.0.19041.56)：遵循该微软 SDK 包所链接的 Windows SDK 许可（https://aka.ms/WinSDKLicenseURL）。
- WinRT.Runtime 来自 [Microsoft C#/WinRT](https://github.com/microsoft/CsWinRT)，使用 MIT 许可。
- 应用/文件图标来自本机项目自身资源或 Windows Shell，仅按快捷方式指定资源读取；没有打包用户安装程序的图标素材库。
- 四窗格入口为项目矢量绘制，来源说明见 Assets。Windows 名称及相关商标归其权利人；未使用 AirDeck 品牌图形。

MIT License — C#/WinRT

Copyright (c) Microsoft Corporation.

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
