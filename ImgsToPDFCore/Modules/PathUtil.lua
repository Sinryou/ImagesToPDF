local PathUtil = {}
local lfs = require("lfs")
local env = require("Modules.env")

function PathUtil.listDirContents(dirPath)
    local dirContents = {}
    for entry in lfs.dir(dirPath) do
        if entry ~= '.' and entry ~= '..' then
            table.insert(dirContents, entry)
        end
    end
    return dirContents
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

function PathUtil.deleteDir(path_to_dir)
    if env.IS_WINDOWS then
        os.execute('rd /s/q "' .. path_to_dir .. '"')
    else
        os.execute('rm -rd "' .. path_to_dir .. '"')
    end
end

return PathUtil
