# 运行依赖许可证原文

本目录保留已还原NuGet包对应仓库提交中的许可证原文。来源URL和SHA256见sources.json，发布脚本会先校验再复制。Open XML SDK及Framework对应Open-XML-SDK-LICENSE.txt；System.Drawing.Common、System.IO.Packaging、Microsoft.Win32.SystemEvents的开发包对应dotnet-LICENSE.txt。

自包含包另从实际.NET10.0.13运行时包复制LICENSE、THIRD-PARTY-NOTICES.TXT及nuspec。实际发布组件及包归档SHA512、NuGet内容哈希、来源和提交写入DEPENDENCIES.json；发布文件的具体版本及SHA256写入FILES.json。

桌面包使用.NETCore和WindowsDesktop运行时，并共享给CLI入口。桌面运行时提供的System.Drawing.Common、System.IO.Packaging、Microsoft.Win32.SystemEvents实际文件为10.0.13系列；开发锁文件中的10.0.2包引用不等同于便携目录中这些文件的实际版本。CLI转换用固定样本单独验证。

NuGet包归档SHA512与NuGet内容哈希是不同字段，分别保留；不将包缓存旁的摘要文件冒充上游独立签名。此目录不代表Pandoc完整对应源码已核查完成；模板许可另见templates/cn-general/LICENSE。
