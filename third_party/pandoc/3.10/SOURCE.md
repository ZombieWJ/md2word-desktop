# Pandoc3.10源码归档

- 下载日期：2026年10月20日（Asia/Shanghai）。
- 官方仓库：https://github.com/jgm/pandoc
- 对应标签：`3.10`。
- 下载地址：https://github.com/jgm/pandoc/archive/refs/tags/3.10.tar.gz
- 本地文件：`pandoc-3.10-source.tar.gz`。
- 文件大小：8,759,054字节。
- SHA256：`fc82815542c29802d087c25e2c1421146030806b71255ec40fc0e828fe1df877`。
- 源码包内`pandoc.cabal`声明的版本：`3.10`；许可证：`GPL-2.0-or-later`。

## 已完成的检查

压缩包目录可完整读取，共3,012项。已检查`pandoc-3.10/pandoc.cabal`、`pandoc-3.10/COPYING.md`和`pandoc-3.10/COPYRIGHT`存在，并读取包内版本和许可证声明。保留原始压缩包，未修改源码，未执行其中的代码或构建脚本。

SHA256为下载后的本地计算结果，用于后续检查文件是否发生变化；本次未取得上游独立校验值或签名进行比对。

## 与现有程序的关系

当前项目中的`pandoc-3.10/pandoc.exe`实测版本为3.10，其SHA256为`0e5d21f8b10537048ad5ba31d575d0792286fe682172fe67ea7267bccd1cabf8`。版本号一致不代表本次已证明该程序由此源码包构建。

本归档用于便携包发布准备。正式分发前仍需核对实际二进制来源、所含依赖的对应源码及构建材料，并保留许可证和版权说明。本归档不表示已完成完整对应源码或整体许可核查。

## 2026年10月21日官方程序核对

从[官方3.10发布资产](https://github.com/jgm/pandoc/releases/expanded_assets/3.10)取得Windows x64 ZIP及其独立公开摘要，并重新下载核对。

- 文件：pandoc-3.10-windows-x86_64.zip，下载地址：https://github.com/jgm/pandoc/releases/download/3.10/pandoc-3.10-windows-x86_64.zip。
- 官方公开SHA256：`bb808d00fd58762299d64582a9b4c3e4b106cd929e62c5f19bcdcb496f1e54ae`，本地下载比对一致。
- ZIP内pandoc.exe、COPYING.rtf、COPYRIGHT.txt和MANUAL.html逐项SHA256均与现有目录一致。发布脚本重复执行此核对，并把结果写入包内pandoc/SOURCE.json。

二进制来源核对已完成；尚未据此证明完整对应源码材料齐全。标签源码包含cabal.project、stack.yaml、Windows打包文件和.github/workflows/release-candidate.yml。后者使用GHC9.10、Cabal3.14及embed_data_files/server/lua标志，并在构建前执行cabal update；这是标签中的构建配置，不是本次二进制的完整构建日志或精确依赖锁定清单。stack.yaml的LTS快照不能直接充当该Cabal构建的实际依赖清单。

后续仍需确认所分发程序全部依赖的精确版本、源码及必要构建材料。当前不把标签源码包标记为完整对应源码，也不以单独上游链接替代最终Release随附材料。
