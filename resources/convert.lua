local cn_general = false

local function strip_lead_emphasis(block)
  if #block.content == 0 or block.content[1].t ~= 'Strong' then return block end
  local lead = block.content[1]
  local text = pandoc.utils.stringify(lead)
  local following = block.content[2]
  local next_text = following and pandoc.utils.stringify(following) or ''
  if text:sub(-1) == ':' or text:sub(-3) == '：' or next_text:sub(1, 1) == ':' or next_text:sub(1, 3) == '：' then
    local inlines = pandoc.Inlines(lead.content)
    for i = 2, #block.content do inlines:insert(block.content[i]) end
    block.content = inlines
  end
  return block
end

function Pandoc(doc)
  cn_general = pandoc.utils.stringify(doc.meta['md2word-template']) == 'cn-general'
  local title_mode = pandoc.utils.stringify(doc.meta['md2word-title-mode']) == 'first-heading'
  local filename = doc.meta['md2word-filename']
  local promoted = title_mode and #doc.blocks > 0 and doc.blocks[1].t == 'Header' and doc.blocks[1].level == 1
  if title_mode then
    if promoted then
      doc.meta.title = pandoc.MetaInlines(doc.blocks[1].content)
      doc.blocks:remove(1)
      doc = doc:walk({ Header = function(h) h.level = math.max(1, h.level - 1); return h end })
    else
      doc.meta.title = filename
    end
  else
    doc.meta.title = nil
  end
  doc.meta['md2word-template'] = nil
  doc.meta['md2word-title-mode'] = nil
  doc.meta['md2word-filename'] = nil
  if cn_general then
    -- 只调整顶层列表；嵌套列表、表格、代码的结构不随顶层转换而重写。
    for i, block in ipairs(doc.blocks) do
      if block.t == 'BulletList' then
        doc.blocks[i] = pandoc.OrderedList(block.content, pandoc.ListAttributes(1, 'Decimal', 'Period'))
      elseif block.t == 'OrderedList' then
        block.listAttributes = pandoc.ListAttributes(1, 'Decimal', 'Period')
      end
    end
    -- 不进入表头、引用或代码，只检查正文和列表开头的引导短语。
    for _, block in ipairs(doc.blocks) do
      if block.t == 'Para' or block.t == 'Plain' then
        strip_lead_emphasis(block)
      elseif block.t == 'OrderedList' or block.t == 'BulletList' then
        for _, item in ipairs(block.content) do
          if #item > 0 and (item[1].t == 'Para' or item[1].t == 'Plain') then strip_lead_emphasis(item[1]) end
        end
      end
    end
  end
  return doc
end
