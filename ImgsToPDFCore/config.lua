local common = require("Modules.Common")
local IO = CS.System.IO
local iPageSize = CS.iTextSharp.text.PageSize
local iRectangle = CS.iTextSharp.text.Rectangle
local commonUtils = CS.ImgsToPDFCore.CommonUtils
local lfs = require("lfs")
local unicode = require("Modules.unicode")

-- add your local funcs below
-- 建议在这个部分添加你自己要用到的函数
local function getChildImgsAndDirs(dirPath)
    local imageExtensions = { ".png", ".apng", ".jpg", ".jpeg", ".jfif", ".pjpeg", ".pjp", ".bmp", ".tif", ".tiff", ".gif", ".webp" }
    local imgPaths = {}
    local dirPaths = {}
    for entry in lfs.dir(dirPath) do
        if entry ~= '.' and entry ~= '..' then
            local path = dirPath .. '/' .. entry
            local attr = lfs.attributes(path)
            assert(type(attr) == 'table')
            if attr.mode == 'directory' then
                table.insert(dirPaths, path)
            elseif common.hasVal(imageExtensions, path:match("(%.%w-)$")) then
                table.insert(imgPaths, path)
            end
        end
    end
    return imgPaths, dirPaths
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
    return outputDir .. [[\]] .. pdfFileName .. ".pdf"
end

-- page size of the output pdf file
-- 输出PDF档的页大小
-- @type iTextSharp.text.Rectangle
-- e.g. Config.PageSizeToSave = iPageSize.A4 (支持NoResize, A0~A10, B0~B10等)
-- 或 Config.PageSizeToSave = iRectangle(0, 0, width, height)
Config.PageSizeToSave = iPageSize.NoResize

-- func that you can order your input files
-- 图片文件排序的方法，默认会去找文件名中的数字部分来进行排序
-- @param path1, path2: string; Full file path of the files to compare.
-- @return: int; If negative, file in path1 will be added to your pdf first.
function Config:FilePathComparer(filePath1, filePath2)
    -- 从完整路径中截取文件名
    local pattern = [[.+[/\](.+)]]
    local fileName1, fileName2 = filePath1:match(pattern) or "", filePath2:match(pattern) or ""
    -- 获取文件名中的数字部分
    pattern = "(%d+).-%."
    local numInPath1, numInPath2 = tonumber(fileName1:match(pattern)), tonumber(fileName2:match(pattern))

    if not numInPath1 and not numInPath2 then -- 如果二者都没有找到数字部分，采用默认排序决策
        return fileName1 == fileName2 and 0 or fileName1 < fileName2 and -1 or 1
    else                                      -- 若其中之一无数字，无数字者在前；否则数字小者在前
        return (numInPath1 or -1) - (numInPath2 or -1)
    end
end

local tempExtraPath
-- this func will be processed before pdf generation start
-- 定义开始前要进行的动作
function Config:PreProcess(...)
    local path, layout, fastFlag = table.unpack({...})
    local compressSuffix = { ".zip", ".rar", ".7z" }
    if IO.Directory.Exists(path) then   -- 如果是文件夹
        pdfFileName = IO.DirectoryInfo(path).Name
        outputDir = path
        CS.ImgsToPDFCore.PDFWrapper.ImagesToPDF(path, layout, fastFlag)
        return  -- 不以压缩格式结尾 不做动作
    elseif not common.hasVal(compressSuffix, IO.Path.GetExtension(path):lower()) then
        return
    end

    pdfFileName = IO.Path.GetFileNameWithoutExtension(path)
    outputDir = IO.Path.GetDirectoryName(path)
    tempExtraPath = path:gsub("%" .. IO.Path.GetExtension(path) .. "$", "") .. os.date("%Y%m%d%H%M%S")
    if not commonUtils.Decompress(path, tempExtraPath) then
        local password = CS.Microsoft.VisualBasic.Interaction.InputBox("Input password:",
            "Encrypted Compress File")
        if common.isEmpty(password) or not commonUtils.Decompress(path, tempExtraPath, password) then
            IO.Directory.Delete(tempExtraPath, true)
            return
        end
    end

    local childImgs, childDirs = getChildImgsAndDirs(unicode.u2a(tempExtraPath))
    if not next(childImgs) then
        if next(childDirs) then
            CS.ImgsToPDFCore.PDFWrapper.ImagesToPDF(unicode.a2u(childDirs[1]), layout, fastFlag)
        end
        return
    end
    CS.ImgsToPDFCore.PDFWrapper.ImagesToPDF(tempExtraPath, layout, fastFlag)
end

-- this func will be processed after your pdf generated
-- 定义结束后要进行的动作
function Config:PostProcess()
    if tempExtraPath then
        IO.Directory.Delete(tempExtraPath, true)
    end
end

return Config
