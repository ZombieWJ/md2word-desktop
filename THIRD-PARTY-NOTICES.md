# 第三方材料与许可边界

根目录MIT许可证适用于本项目自有应用源码及自写说明，不替换以下第三方材料的许可证。

| 材料 | 当前版本 | 许可或状态 |
|---|---|---|
| Pandoc及随附组件 | 3.10 | GPL v2或更高版本，另有组件版权说明；保留COPYING.rtf和COPYRIGHT.txt。 |
| DocumentFormat.OpenXml及Framework | 3.5.1 | 已还原包的nuspec声明MIT。 |
| System.IO.Packaging | 10.0.2 | 已还原包的nuspec声明MIT。 |
| System.Drawing.Common、Microsoft.Win32.SystemEvents | 10.0.2 | 已还原包的nuspec声明MIT；用于Windows上的本地图片解码验证，不依赖Word。 |
| .NET SDK | 10.0.402 | 仅为开发工具，不随应用分发；实际运行时许可收集情况见下文。 |
| CN-General参考文件及预览 | 见资源来源记录 | 项目所有者自制，采用MIT，独立许可及来源见templates/cn-general。 |

测试依赖不随应用分发：Microsoft.NET.Test.Sdk、Microsoft.CodeCoverage、Microsoft.TestPlatform.ObjectModel和TestHost为17.14.1，nuspec声明MIT；Newtonsoft.Json13.0.3声明MIT；xUnit2.9.3、其分析器1.18.0及VS适配器3.0.2声明Apache-2.0。xunit.abstractions2.0.3未在nuspec中提供license表达式，尚未单独完成许可证文本核对。

全部直接与传递版本及内容哈希记录在各项目的packages.lock.json中。这是开发阶段盘点，不代表所有发布义务已经完成。

正式便携包将包含Pandoc。发布前必须核对所分发二进制的来源、完整对应源码、所含依赖和构建材料；现有标签源码归档不直接等同于完整对应源码。相关材料与便携包在同一Release提供下载，并保留版权和许可证。自有代码采用MIT不构成对最终组合发行边界的法律结论。

不分发微软雅黑字体文件。CN-General为项目所有者自制模板，按MIT授权；许可和来源见templates/cn-general/LICENSE及SOURCE.md。自有模板许可不替代第三方组件许可。

安装包由Inno Setup7.1.0构建，工具来自官方签名下载，版本与摘要见release/inno.json。编译工具以官方便携模式放在.tools/inno，不随应用分发；安装引擎的版权及必要声明保留，工具许可证随候选包附在licenses/Inno-Setup.txt。官方要求商业使用者购买商业许可，项目当前为免费开源作品，后续用途变化时重新核对条款。

## 便携候选包已收集的材料

发布脚本从实际发布依赖收集.NETCore/WindowsDesktop运行时10.0.13的LICENSE及Core运行时第三方声明，从固定仓库提交保留Open XML SDK许可证，并附相关nuspec。包内DEPENDENCIES.json记录实际发布组件、来源、提交、NuGet内容哈希及包归档摘要；FILES.json记录每个发布文件的SHA256和实际版本。

便携目录中的System.Drawing.Common、System.IO.Packaging和Microsoft.Win32.SystemEvents由桌面运行时提供10.0.13系列文件，共享给CLI。上表10.0.2为开发包引用；不能据此认定便携目录中也存在这些版本的独立包。测试工具和SDK不随便携包发布。

Pandoc官方Windows ZIP的公开SHA256与重新下载结果一致，四个原始文件均逐项一致；来源见third_party/pandoc/3.10/SOURCE.md。程序来源已核对，完整对应源码、全部依赖及必要构建材料仍待补齐。候选包及source-materials均保留此状态，不作为可正式公开发布的结论。
