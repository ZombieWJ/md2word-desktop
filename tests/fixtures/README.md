# 固定测试输入

本目录包含16份固定合成输入，不代表真实业务需求。basic.md保留原始输入。assets中的PNG/JPEG是自制示意图，没有引入第三方图片或字体文件；broken.png为故意损坏的测试文件，unsupported.svg用于验证拒绝不支持的格式。

| 文件 | 主要覆盖 |
|---|---|
| basic.md | 中文、主标题、普通列表、基础表格、链接和技术符号。 |
| headings.md | 标题提取、后续一级标题、深层标题及原有章节编号。 |
| lists.md | 两位数编号实际换行、每组重启、顶层无序转编号、嵌套类型及强调。 |
| tables.md | 内容分配列宽、左右居中对齐、表头、单元格边距、白底黑框。 |
| code-links.md | 代码中的Markdown标记、行内代码、链接目标、强调和引用。 |
| local-images.md | 本地PNG/JPEG嵌入、图片前后内容、图片段落自动行高。 |
| large-image.md | 纵向长图按比例缩小，完整显示首尾及12段图形。 |
| images-in-table.md | 表内图片列宽和单元格边界。 |
| 中文 空格.md | 中文与空格路径、URL编码图片路径、技术符号和强调。 |
| long-text.md | 6节长文，中英文混排及跨页内容保留。 |
| wide-table.md | 8列宽表、单位、地址和链接。 |
| long-table.md | 40条数据、跨页重复表头及结束文字。 |
| warnings.md | 网络图片不下载，保留原说明；HTML与Mermaid原文保留并警告。 |
| missing-image.md | 缺少本地图片，预期失败且不输出DOCX。 |
| unsupported-image.md | 本地SVG，预期失败并提示PNG/JPEG。 |
| corrupt-image.md | 损坏PNG，预期失败并提示重新另存图片。 |

构建入口scripts/build.ps1运行真实Pandoc转换测试，默认报告为artifacts/test-results/latest.trx；本阶段使用-TestReport stage2保存到stage2.trx。逐次转换测试的DOCX位于artifacts/test-runs中。文件占用、并发重名、无权限、模板损坏、扩展名伪装和取消等由自动测试另外创建输入。


这些样本不作为无需复核的永久排版标准。GFM强调语法的中文冒号旁使用空格使分隔符明确；转换器不通过正则改写用户的Markdown语法。

当前检查范围见[当前状态](../../docs/开发计划与验收.md)。在新的输出目录中运行样例，不以已有DOCX自动充当期望结果。
