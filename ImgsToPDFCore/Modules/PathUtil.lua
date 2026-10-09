local lfs <const> = require("lfs")

local PathUtil = {}

-- Lua 5.5 table.create 兼容层
if not table.create then
    rawset(table, "create", function(narr, nrec) return {} end)
end

function PathUtil.currentDir()
    return lfs.currentdir()
end

function PathUtil.dirExist(path)
    if not path then return false end
    local attr <const> = lfs.attributes(path)
    return type(attr) == "table" and attr.mode == "directory"
end

function PathUtil.fileExist(path)
    if not path then return false end
    local attr <const> = lfs.attributes(path)
    return type(attr) == "table" and attr.mode == "file"
end

function PathUtil.fileName(path)
    if not path then return nil end
    return path:match("([^/\\]+)$") or path
end

function PathUtil.getExtension(path)
    if not path then return nil end
    local name <const> = PathUtil.fileName(path)
    if not name then return nil end
    return name:match("(%.[^.]+)$")
end

function PathUtil.fileNameWithoutExtension(path)
    local name <const> = PathUtil.fileName(path)
    if not name then return nil end
    local ext <const> = PathUtil.getExtension(name)
    if not ext then return name end
    return name:sub(1, #name - #ext)
end

function PathUtil.dirPath(path)
    if not path then return nil end
    local clean <const> = path:match("^(.-)[/\\]?$")
    if PathUtil.dirExist(clean) then
        return clean
    end
    if PathUtil.fileExist(clean) or PathUtil.getExtension(clean) then
        local parent <const> = clean:match("^(.*)[/\\][^/\\]+$")
        return (parent and parent ~= "") and parent or "."
    end
    return clean
end

function PathUtil.dirName(path)
    if not path then return nil end
    local dp <const> = PathUtil.dirPath(path)
    if not dp then return nil end
    local clean <const> = dp:match("^(.-)[/\\]?$")
    return clean:match("([^/\\]+)$") or clean
end

function PathUtil.listDirContents(dirPath)
    if not dirPath or not PathUtil.dirExist(dirPath) then
        return nil, "Directory does not exist: " .. tostring(dirPath)
    end
    local dirContents <const> = table.create(16, 0)
    local ok, iter, dir_obj = pcall(lfs.dir, dirPath)
    if not ok then
        return nil, iter
    end
    for entry in iter, dir_obj do
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
    if not root_path or not PathUtil.dirExist(root_path) then
        return {}
    end
    local folders <const> = table.create(16, 0)

    -- 内部递归辅助函数
    local function traverseFolder(current_path)
        local ok, iter, dir_obj = pcall(lfs.dir, current_path)
        if not ok then return end
        for entry in iter, dir_obj do
            if entry ~= "." and entry ~= ".." then
                local full_path <const> = current_path .. "/" .. entry
                local attr <const> = lfs.attributes(full_path)

                if attr and attr.mode == "directory" then
                    local final_path <const> = (type(processor_func) == "function")
                        and processor_func(full_path)
                        or full_path
                    table.insert(folders, final_path)
                    traverseFolder(full_path)
                end
            end
        end
    end

    traverseFolder(root_path)
    return folders
end

function PathUtil.deleteDir(rootpath)
    if not rootpath or not PathUtil.dirExist(rootpath) then
        return false, "Directory does not exist: " .. tostring(rootpath)
    end

    local function deleteEntry(path)
        local attr <const> = lfs.attributes(path)
        if not attr then return false end
        if attr.mode == 'directory' then
            local ok, iter, dir_obj = pcall(lfs.dir, path)
            if ok then
                for entry in iter, dir_obj do
                    if entry ~= '.' and entry ~= '..' then
                        deleteEntry(path .. '/' .. entry)
                    end
                end
            end
            return lfs.rmdir(path)
        else
            return os.remove(path)
        end
    end

    local ok, iter, dir_obj = pcall(lfs.dir, rootpath)
    if not ok then return false, iter end
    for entry in iter, dir_obj do
        if entry ~= '.' and entry ~= '..' then
            deleteEntry(rootpath .. '/' .. entry)
        end
    end
    return lfs.rmdir(rootpath)
end

return PathUtil
