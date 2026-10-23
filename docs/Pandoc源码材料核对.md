# Pandoc源码材料

项目使用未修改的官方Pandoc3.10 Windows x64程序，作为独立进程运行。当前已核对二进制来源，完整对应源码与构建材料仍待补齐。

## 已归档材料

- 官方Windows ZIP，SHA256：`bb808d00fd58762299d64582a9b4c3e4b106cd929e62c5f19bcdcb496f1e54ae`。
- pandoc.exe、COPYING.rtf、COPYRIGHT.txt和MANUAL.html均与该ZIP逐项一致。
- 3.10标签源码归档，SHA256：`fc82815542c29802d087c25e2c1421146030806b71255ec40fc0e828fe1df877`，包含版权、许可、Cabal项目及Windows构建配置。

具体来源见[第三方来源记录](../third_party/pandoc/3.10/SOURCE.md)，程序与源码校验值由release/portable.json固定。归档不进入源码Git记录，开发准备及打包脚本按固定来源取得并校验。

## 软件发行前待补齐

1. 将所用Windows ZIP关联到精确源码提交、构建任务和参数。
2. 精确GHC/Cabal版本、完整依赖计划、所用依赖源码及必要补丁、数据和许可。
3. 可说明如何重建该二进制的材料，并核对最终下载附件的实际内容。

标签中的构建配置使用GHC9.10、Cabal3.14系列及embed_data_files/server/lua选项，包含cabal update和依赖版本范围。配置存在不等于已取得官方二进制所用的全部精确版本，标签归档也不能直接视为完整对应源码合集。

对应材料未齐全时，release/public-readiness.json中的检查保持待完成，不提供包含该程序的正式软件下载。程序来源核对与完整源码核对分别记录，不能互相替代。

## 自行构建路线

新增手动Windows构建入口[源码构建工作流](../.github/workflows/pandoc-source.yml)。固定Pandoc3.10源码摘要、GHC9.10.3、Cabal3.14.2.0及依赖索引时间，按安全索引核对依赖源码与Cabal修订，再关闭远程依赖仓库从归档中的本地源码构建。

流程计划同时保存实际构建计划、编译器包记录、许可证、GHC及Windows配套源码。二进制与源码材料同时归档，材料不放入用户安装目录。构建及材料检查成功前，现有官方二进制的检查仍保持待完成，不提前认定新流程通过；它也不证明新产物与官方二进制逐字节一致。
