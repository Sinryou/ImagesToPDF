local PathUtil = {}
local lfs = require("lfs")

function PathUtil.listDirContents(dirPath)
    local dirContents = {}
    for entry in lfs.dir(dirPath) do
        if entry ~= '.' and entry ~= '..' then
            table.insert(dirContents, entry)
        end
    end
    return dirContents
end

--- 递归获取所有子文件夹路径
-- @param root_path 根目录路径
-- @param processor_func (可选) 对路径进行处理的函数
-- @return 包含所有子文件夹路径的 table
function PathUtil.listSubfolders(root_path, processor_func)
    local folders = {}

    -- 内部递归辅助函数
    local function traverseFolder(current_path)
        -- 遍历当前目录下的所有条目
        for entry in lfs.dir(current_path) do
            if entry ~= "." and entry ~= ".." then
                local full_path = current_path .. "/" .. entry
                local attr = lfs.attributes(full_path)

                -- 如果是文件夹则递归
                if attr and attr.mode == "directory" then
                    -- 如果存在处理函数，则处理后再加入列表
                    local final_path = full_path
                    if type(processor_func) == "function" then
                        final_path = processor_func(full_path)
                    end
                    
                    table.insert(folders, final_path)
                    
                    traverseFolder(full_path)
                end
            end
        end
    end

    traverseFolder(root_path)
    return folders
end

function PathUtil.dirExist(path)
    local attr = lfs.attributes(path)
    return type(attr) == "table" and attr.mode == "directory"
end

function PathUtil.fileExist(path)
    local attr = lfs.attributes(path)
    return type(attr) == "table" and attr.mode == "file"
end

function PathUtil.dirName(path)
    return path:match([[.+[/\]([^/\]+)[/\]?$]])
end

function PathUtil.dirPath(path)
    if path:find("%.%w+$") then
        return path:match([[^(.+)[/\][^/\]*%.%w+$]])
    else
        return path:match([[^(.-)[/\]?$]])
    end
end

function PathUtil.fileName(path)
    return path:match([[.+[/\]([^/\]+)$]])
end

function PathUtil.getExtension(path)
    return path:match("(%.%w+)$")
end

function PathUtil.fileNameWithoutExtension(path)
    return PathUtil.fileName(path):sub(1, -(1 + #(PathUtil.getExtension(path) or "")))
end

function PathUtil.deleteDir(rootpath)
    local function deleteEntry(entry)
        local path = rootpath .. '/' .. entry
        local attr = lfs.attributes(path)
        assert(type(attr) == 'table', "Failed to get attributes for " .. path)

        if attr.mode == 'directory' then
            PathUtil.deleteDir(path)
        else
            assert(os.remove(path), "Failed to remove file " .. path)
        end
    end

    for entry in lfs.dir(rootpath) do
        if entry ~= '.' and entry ~= '..' then
            deleteEntry(entry)
        end
    end
    assert(lfs.rmdir(rootpath), "Failed to remove directory " .. rootpath)
end

return PathUtil
