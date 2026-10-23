# 可复现的展示样例

首页展示3份合成Markdown、项目介绍图和工具实际生成的DOCX。首例为从零编写的虚构工作周报，不使用已有真实业务文档；所有角色和数量均为演示数据。

| 输入 | 结果 | 检查重点 |
|---|---|---|
| [work-report.md](work-report.md) | [work-report.docx](output/work-report.docx) | 工作周报，含1、2、3、3.1、3.2分级标题，第一页包含工作进展表，下周安排说明反馈处理与流程复查。 |
| [table.md](table.md) | [table.docx](output/table.docx) | 表头、边框、单元格留白与表后正文。 |
| [mixed-images.md](mixed-images.md) | [mixed-images-numbered.docx](output/mixed-images-numbered.docx) | 带序号的章节标题，中英文、80µm、≤5、0.60 L/min、0xFF、0b1010、链接、代码和本地图片。 |

更多示例：[编号专项输入](numbering.md)与[DOCX结果](output/numbering.docx)，用于补充查看两位数编号、续行及嵌套列表；不列入当前演示的3份输入。

## 复现

在项目根目录按开发指南构建后运行：

```powershell
./scripts/convert.ps1 -InputFiles @('examples/showcase/work-report.md','examples/showcase/table.md','examples/showcase/mixed-images.md') -Template cn-general -OutputDirectory artifacts/showcase/reproduced
```

复制输入时保留assets/md2word-cover.png与Markdown的相对路径。此图与项目分享封面相同，按原始字节嵌入；文件摘要见展示清单。不要直接在output中反复运行命令覆盖展示文件；项目会对重名结果另存。

中文通用允许的格式变化：标题提取为文档标题；后续标题调整层级；顶层无序事项转为数字编号；正文及列表开头的引导词取消加粗；表格和字体统一。文字、顺序、数值和链接不应被改写。

周报的章节号直接写在Markdown标题中：二级标题如`## 3 下周安排`，三级标题如`### 3.1 处理剩余反馈`。转换后分别使用Word的一级、二级标题样式，保留输入序号，不额外叠加章节号。图文示例使用1、2、3编号的章节标题。0xFF和0b1010也是输入文字，转换时保留原写法，不自动换算进制。

## 检查依据

实际运行核心生成结果，使用Microsoft Word 15.0 / Build15.0.5589只读打开，导出PDF后逐页检查。当前3份PDF各1页；周报进展表完整出现在第一页，图文页包含完整项目介绍图。Word页数接口曾暂报2、1、2页，与随后导出的PDF不同，因此展示记录按实际PDF页数，不采用该瞬时统计值。

各输入、DOCX和页面图片的SHA256见[showcase.json](../../docs/assets/showcase.json)。[展示验证记录](../../docs/展示验证记录.md)区分控件采帧、结构检查与Word渲染；固定样例不能代表所有输入。

正文为合成内容，项目介绍图及CN-General模板由项目所有者提供，按项目MIT许可使用。其他依赖保留各自许可。
