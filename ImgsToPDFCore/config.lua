local lfs = require("lfs")

local common = require("Modules.Common")
local pathUtil = require("Modules.PathUtil")
local unicode = require("Modules.unicode")
local osUtil = require("Modules.OSUtil")
local u2a = unicode.u2a
local a2u = unicode.a2u

local iPageSize = CS.iTextSharp.text.PageSize
local iRectangle = CS.iTextSharp.text.Rectangle
local commonUtils = CS.ImgsToPDFCore.CommonUtils
local PDFWrapper = CS.ImgsToPDFCore.PDFWrapper
local interaction = CS.Microsoft.VisualBasic.Interaction

-- add your local funcs below
-- 建议在这个部分添加你自己要用到的函数
local function getChildImgsAndDirs(dirPath)
    local imageExtensions = { ".png", ".apng", ".jpg", ".jpeg", ".jfif", ".pjpeg", ".pjp", ".bmp", ".tif", ".tiff",
        ".gif", ".webp" }
    local dirPaths = {}
    local hasImg = false
    for entry in lfs.dir(dirPath) do
        if entry ~= '.' and entry ~= '..' then
            local path = dirPath .. '/' .. entry
            local attr = lfs.attributes(path)
            assert(type(attr) == 'table')
            if attr.mode == 'directory' then
                table.insert(dirPaths, path)
            elseif common.hasVal(imageExtensions, (pathUtil.getExtension(path) or ""):lower()) then
                hasImg = true
                break -- 调用方只需要知道“是否存在图片”，找到第一张即可提前结束遍历
            end
        end
    end
    return hasImg, dirPaths
end

-- 合并pdf
local function mergePdfs(path)
    local pdf2MergeList = {}
    if path and pathUtil.dirExist(u2a(path)) then
        pdf2MergeList = pathUtil.listSubfolders(u2a(path),function(p) return a2u(p.."/"..pathUtil.dirName(p) .. ".pdf") end)
    end
    table.insert(pdf2MergeList, path.."/"..pathUtil.dirName(path) ..".pdf")
    -- PDFWrapper.PdfMerge(pdf2MergeList, path.."/"..pathUtil.dirName(path) .."_Merged.pdf")
    -- PDFWrapper.PdfMergeWithHierarchicalOutlines(pdf2MergeList, path.."/"..pathUtil.dirName(path) .."_Merged.pdf")
    PDFWrapper.PdfMergeWithDeepOutlines(pdf2MergeList, path.."/"..pathUtil.dirName(path) .."_Merged.pdf", path)
end

-- 排序辅助缓存：排序过程中同一个路径会被比较 O(log n) 次，
-- 原实现每次都重新做字符串匹配和版本号解析，这里缓存结果避免重复计算。
local pathNameCache = {}   -- 完整路径 -> 去扩展名的文件名
local versionCache = {}    -- 文件名 -> 版本号数组（false 表示没有版本号）

local function getBaseName(filePath)
    local name = pathNameCache[filePath]
    if name == nil then
        name = pathUtil.fileNameWithoutExtension(filePath) or ""
        pathNameCache[filePath] = name
    end
    return name
end

-- 将文件名中所有连续数字段提取为版本数组；没有数字时返回 false。
-- 旧实现只取"第一个数字+点号的连续串"（[%d%.]+），遇到下划线/连字符/中文等
-- 分隔符会被截断，且文件名前部出现数字时会抢走版本号，导致排序退化。
-- 这里对任意分隔符都适用：
--   "v1.10.1"   -> {1, 10, 1}
--   "a_1_2_3"   -> {1, 2, 3}
--   "a-1-2-3"   -> {1, 2, 3}
--   "第1话 v1.2" -> {1, 1, 2}   （文件名里出现的所有数字都参与比较）
local function getVersionArray(name)
    local cached = versionCache[name]
    if cached ~= nil then
        return cached
    end
    local version = {}
    -- 匹配文件名中的每一个数字段
    for part in name:gmatch("%d+") do
        table.insert(version, tonumber(part))
    end
    if #version == 0 then
        version = false
    end
    versionCache[name] = version
    return version
end

-------------------------------------------------------------------
----***************************************************************
----Config for how to generate your images to pdf file
----图片转PDF的配置
----***************************************************************
-------------------------------------------------------------------

local Config = {}

-- the path to save your output pdf file
-- 输出PDF档的保存路径
-- @type string
local pdfFileName
local outputDir
function Config.PathToSave()
    return table.concat({ outputDir, "/", pdfFileName, ".pdf" })
end

-- page size of the output pdf file
-- 输出PDF档的页大小
-- @type iTextSharp.text.Rectangle
-- e.g. Config.PageSizeToSave = iPageSize.A4 (支持NoResize, A0~A10, B0~B10等)
-- 或 Config.PageSizeToSave = iRectangle(0, 0, width, height)
Config.PageSizeToSave = iPageSize.NoResize

-- func that you can order your input files
-- 图片文件排序的方法：提取文件名中的数字段做自然排序，
-- 数字段之间的分隔符不限（. _ - 空格、中文等均可），没有数字的文件排在最前
-- @param path1, path2: string; Full file path of the files to compare.
-- @return: int; If negative, file in path1 will be added to your pdf first.
function Config:FilePathComparer(filePath1, filePath2)
    local fileName1 = getBaseName(filePath1)
    local fileName2 = getBaseName(filePath2)

    local v1 = getVersionArray(fileName1)
    local v2 = getVersionArray(fileName2)

    if not v1 and not v2 then
        if filePath1 == filePath2 then return 0 end
        return filePath1 < filePath2 and -1 or 1
    elseif not v1 then return -1
    elseif not v2 then return 1
    end

    -- 核心逻辑：按位比较数组
    local maxLen = math.max(#v1, #v2)
    for i = 1, maxLen do
        local n1 = v1[i] or 0 -- 如果长度不足，补0比较（例如 1.1 和 1.1.1）
        local n2 = v2[i] or 0
        
        if n1 < n2 then return -1 end
        if n1 > n2 then return 1 end
    end

    -- 如果数字部分完全一致，则按完整字符串排序
    return filePath1 == filePath2 and 0 or (filePath1 < filePath2 and -1 or 1)
end

local tempExtraPath
-- this func will be processed before pdf generation start
-- 定义开始前要进行的动作
function Config:PreProcess(...)
    local path, layout, fastFlag, merge = ...
    -- 每次任务开始前清空排序缓存，避免跨任务残留
    pathNameCache = {}
    versionCache = {}
    local compressSuffix = { ".zip", ".rar", ".7z" }
    if pathUtil.dirExist(u2a(path)) then -- 如果是文件夹
        if merge then
            return mergePdfs(path)
        end
        pdfFileName = pathUtil.dirName(path)
        outputDir = path
        PDFWrapper.ImagesToPDF(path, layout, fastFlag)
        return
    elseif not common.hasVal(compressSuffix, (pathUtil.getExtension(path) or ""):lower()) then
        return -- 不以压缩格式结尾 不做动作
    end

    pdfFileName = pathUtil.fileNameWithoutExtension(path) or "Output"
    outputDir = pathUtil.dirPath(path)
    tempExtraPath = outputDir .. "/" .. pdfFileName .. os.date("%Y%m%d%H%M%S")
    if not commonUtils.Decompress(path, tempExtraPath) then
        local password = interaction.InputBox("Input password:", "Encrypted Compress File")
        if common.isEmpty(password) or not commonUtils.Decompress(path, tempExtraPath, password) then
            return
        end
    end

    local hasChildImgs, childDirs = getChildImgsAndDirs(u2a(tempExtraPath))
    if not hasChildImgs then
        if next(childDirs) then
            PDFWrapper.ImagesToPDF(a2u(childDirs[1]), layout, fastFlag)
        end
        return
    end
    PDFWrapper.ImagesToPDF(tempExtraPath, layout, fastFlag)
end

-- this func will be processed after your pdf generated
-- 定义结束后要进行的动作
function Config:PostProcess()
    if tempExtraPath and pathUtil.dirExist(u2a(tempExtraPath)) then
        pathUtil.deleteDir(u2a(tempExtraPath))
    end
end

return Config
