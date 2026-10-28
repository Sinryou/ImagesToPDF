local common = require("Modules.Common")

local iPageSize = CS.iTextSharp.text.PageSize
local iRectangle = CS.iTextSharp.text.Rectangle
local commonUtils = CS.ImgsToPDF.Core.CommonUtils
local PDFWrapper = CS.ImgsToPDF.Core.PDFWrapper
local csPath = CS.System.IO.Path
local csDirectory = CS.System.IO.Directory
local csSearchOption = CS.System.IO.SearchOption

-- add your local funcs below
-- 建议在这个部分添加你自己要用到的函数

-- 合并pdf
local function mergePdfs(path)
    path = (path or ""):gsub("[/\\]+$", "")

    if not csDirectory.Exists(path) then return end

    local pdf2MergeList = {}
    local subDirs = csDirectory.GetDirectories(path, "*", csSearchOption.AllDirectories)
    for i = 0, subDirs.Length - 1 do
        local p = subDirs[i]
        local dirName = csPath.GetFileName(p)
        table.insert(pdf2MergeList, p .. "/" .. dirName .. ".pdf")
    end

    local rootDirName = csPath.GetFileName(path)
    table.insert(pdf2MergeList, path .. "/" .. rootDirName .. ".pdf")
    -- PDFWrapper.PdfMerge(pdf2MergeList, path.."/"..rootDirName .."_Merged.pdf")
    -- PDFWrapper.PdfMergeWithHierarchicalOutlines(pdf2MergeList, path.."/"..rootDirName .."_Merged.pdf")
    PDFWrapper.PdfMergeWithDeepOutlines(pdf2MergeList, path .. "/" .. rootDirName .. "_Merged.pdf", path)
end

-- 排序辅助缓存：排序过程中同一个路径会被比较 O(log n) 次，
-- 原实现每次都重新做字符串匹配和版本号解析，这里缓存结果避免重复计算。
local pathNameCache = {} -- 完整路径 -> 去扩展名的文件名
local versionCache = {}  -- 文件名 -> 版本号数组（false 表示没有版本号）

local function getBaseName(filePath)
    local name = pathNameCache[filePath]
    if name == nil then
        if not filePath or filePath == "" then
            name = ""
        else
            name = csPath.GetFileNameWithoutExtension(filePath) or ""
        end
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
    if not outputDir or not pdfFileName then return nil end
    return table.concat({ outputDir, "/", pdfFileName, ".pdf" })
end

-- page size of the output pdf file
-- 输出PDF档的页大小
-- @type iTextSharp.text.Rectangle
-- e.g. Config.PageSizeToSave = iPageSize.A4 (支持NoResize, A0~A10, B0~B10等)
-- 或 Config.PageSizeToSave = iRectangle(0, 0, width, height)
Config.PageSizeToSave = iPageSize.NoResize

-- 开启 --fast 时的 JPEG 压缩质量（1~100，默认 75；数值越小体积越小，数值越大画质越好）
-- @type int
Config.FastQuality = 75

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
    elseif not v1 then
        return -1
    elseif not v2 then
        return 1
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

-- 递归收集子文件夹
local function collectSubfolders(rootPath, out)
    if not rootPath or not csDirectory.Exists(rootPath) then return end
    local ok, dirsCs = pcall(csDirectory.GetDirectories, rootPath)
    if not ok or not dirsCs then return end
    local subDirs = {}
    for i = 0, dirsCs.Length - 1 do
        table.insert(subDirs, dirsCs[i])
    end
    for i = 1, #subDirs do
        table.insert(out, subDirs[i])
        collectSubfolders(subDirs[i], out)
    end
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
    if path and csDirectory.Exists(path) then -- 如果是文件夹
        path = path:gsub("[/\\]+$", "")
        pdfFileName = csPath.GetFileName(path) or "Output"
        outputDir = path
        if merge then
            -- 合并分支也要把"预期输出路径"填好：Program 用它校验本次是否真的产出了 PDF，
            -- 命名与 mergePdfs 写出的文件保持一致
            pdfFileName = pdfFileName .. "_Merged"
            return mergePdfs(path)
        end
        PDFWrapper.ImagesToPDF(path, layout, fastFlag)
        return
    elseif commonUtils.IsManifestFile(path) then
        local manifestData = commonUtils.ParseManifest(path)
        if not manifestData or not manifestData.ImagePaths or manifestData.ImagePaths.Length == 0 then
            commonUtils.ReportFailure("No valid files found in the manifest file: " .. tostring(path))
            return
        end
        outputDir = manifestData.OutputDir
        pdfFileName = manifestData.PdfFileName
        -- 检查清单首项是否为 PDF 文件；若是则执行多个 PDF 合并逻辑（调用最基础的普通合并 PdfMerge）
        local firstFile = manifestData.ImagePaths[0] or manifestData.ImagePaths[1]
        local ext = firstFile and csPath.GetExtension(firstFile)
        local isPdf = ext and ext:lower() == ".pdf"

        if isPdf then
            local outputPath = Config.PathToSave()
            PDFWrapper.PdfMerge(manifestData.ImagePaths, outputPath)
            return
        end
        local pages = PDFWrapper.ImagesToPDFFromImageFiles(manifestData.ImagePaths, layout, fastFlag)
        if pages == 0 and not commonUtils.FailureReported then
            commonUtils.ReportFailure("Failed to write images to PDF: " .. tostring(path))
        end
        return
    elseif not common.hasVal(compressSuffix, (csPath.GetExtension(path) or ""):lower()) then
        -- 不以压缩格式结尾 不做动作；必须上报，否则上层会把"什么都没生成"当成生成成功
        commonUtils.ReportFailure("Not a folder or a supported archive; no PDF was generated: " .. tostring(path))
        return
    end

    local baseName = csPath.GetFileNameWithoutExtension(path)
    pdfFileName = (baseName and baseName ~= "") and baseName or "Output"
    local dir = csPath.GetDirectoryName(path)
    outputDir = (dir and dir ~= "") and dir or "."
    tempExtraPath = outputDir .. "/" .. pdfFileName .. os.date("%Y%m%d%H%M%S")

    -- 只有确实是加密包才去问密码。否则把解压失败的真实原因（条目非法、磁盘错误等）
    -- 直接上报，免得像以前那样一律提示"密码为空或错误"，用户输多少次密码都没用。
    -- IsArchiveEncrypted 同样可能抛异常（例如压缩包被截断），这里一并兜住，
    -- 抛异常时按"判断不出来"处理，直接上报失败而不是让错误逃逸成 LuaException。
    local encryptedOk, encrypted = pcall(commonUtils.IsArchiveEncrypted, path)
    if not encryptedOk then
        commonUtils.ReportFailure("Failed to extract the archive; no PDF was generated: " .. tostring(path))
        return
    end
    if not encrypted then
        -- pcall 的返回值是 (ok, ...)。必须同时检查 ok：C# 抛出的异常会被 XLua 转成
        -- 一个"字符串"错误对象返回，而字符串在 Lua 里为真——只看第二个返回值会把
        -- "解压抛异常"误判成"解压成功"，然后继续在空目录上跑，最后报成"压缩包里没有图片"，
        -- 真正的原因（磁盘错误、父进程退出等）反而被埋掉，解压目录也不会被清理。
        local ok, result = pcall(commonUtils.Decompress, path, tempExtraPath)
        if not ok or not result then
            commonUtils.ReportFailure("Failed to extract the archive: " .. tostring(result))
            return
        end
    else
        local password = commonUtils.PromptPassword(path)
        if common.isEmpty(password) then
            commonUtils.ReportFailure(
                "Extraction cancelled or no password provided; no PDF was generated: " .. tostring(path))
            return
        end
        -- 带密码解压这一路同样可能抛异常，不能让它逃逸
        local pwdOk, pwdResult = false, false
        if not common.isEmpty(password) then
            pwdOk, pwdResult = pcall(commonUtils.Decompress, path, tempExtraPath, password)
        end
        if not pwdOk or not pwdResult then
            -- 密码为空、密码错误或解压抛异常：必须上报，否则上层会把"什么都没生成"当成生成成功
            commonUtils.ReportFailure(
                "Failed to extract the archive (empty or wrong password); no PDF was generated: " .. tostring(path))
            return
        end
    end

    -- 把解压出来的整棵目录树都交给 Core。
    -- 旧实现只看根目录：根目录有图片就只转根目录，否则只转"第一个子目录"，
    -- 其余目录里的图片会被静默丢掉，却依然上报"生成成功"。
    local folders = { tempExtraPath }
    collectSubfolders(tempExtraPath, folders)

    local pages = PDFWrapper.ImagesToPDFFromFolders(folders, layout, fastFlag)
    if pages == 0 and not commonUtils.FailureReported then
        -- 整包里一张图片都没有：必须上报，否则上层会把"什么都没生成"当成生成成功。
        -- 若已经上报过失败（例如输出文件写不进去），就不再补一条会误导人的"没有图片"。
        commonUtils.ReportFailure("No image found in the archive; no PDF was generated: " .. tostring(path))
    end
end

-- this func will be processed after your pdf generated
-- 定义结束后要进行的动作
function Config:PostProcess()
    if tempExtraPath and csDirectory.Exists(tempExtraPath) then
        pcall(csDirectory.Delete, tempExtraPath, true)
    end
end

return Config
