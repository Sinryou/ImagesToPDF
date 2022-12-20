-- add your local funs below
local function givePathToSave()
    -- 在传入程序的文件夹路径下创建output.pdf为默认pdf输出
    return CS.ImgsToPDFCore.CSGlobal.srcDirPath .. [[\output.pdf]]
end

-------------------------------------------------------------------
----***************************************************************
----Config for how to generate your images to pdf file
----***************************************************************
-------------------------------------------------------------------

-- the path to save your output pdf file
-- @type string
PathToSave = givePathToSave()

-- func that you can order your input files
-- @param path1, path2: string; Full file path of the files to compare.
-- @return: int; If negative, file in path1 will be added to your pdf first.
function FilePathComparer(filePath1, filePath2)
    -- 从完整路径中截取文件名
    local pattern = [[.+[/\](.+)]]
    local fileName1, fileName2 = filePath1:match(pattern) or "", filePath2:match(pattern) or ""
    -- 获取文件名中的数字部分
    pattern = "(%d+).-%."
    local numInPath1, numInPath2 = tonumber(fileName1:match(pattern)), tonumber(fileName2:match(pattern))

    if not numInPath1 and not numInPath2 then -- 如果二者都没有找到数字部分，采用默认排序决策
        return fileName1 == fileName2 and 0 or fileName1 < fileName2 and -1 or 1
    else -- 若其中之一无数字，无数字者在前；否则数字小者在前
        return (numInPath1 or -1) - (numInPath2 or -1)
    end
end

-- this func will be processed after your pdf generated
function PostProcess()
    -- local filesInSrcDir = CS.System.IO.Directory.GetFiles(CS.ImgsToPDFCore.CSGlobal.srcDirPath)

    -- for i = 0, filesInSrcDir.Length-1 do
    --     if filesInSrcDir[i]:match("%.tmp$") then
    --         CS.System.IO.File.Delete(filesInSrcDir[i]);
    --     end
    -- end
end
