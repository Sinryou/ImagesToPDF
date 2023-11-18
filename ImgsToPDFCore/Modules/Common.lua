local Common = {}

function Common.isInt(i)
    if type(i) == "number" then
        return i % 1 == 0
    end
    return false
end

function Common.isFloat(i)
    if type(i) == "number" then
        return i % 1 ~= 0
    end
    return false
end

function Common.isEmpty(o)
    if type(o) == "table" then
        return next(o) == nil
    end
    return o == nil or o == ''
end

function Common.len(t)
    if type(t) ~= "table" then
        return #t
    end
    local len = 0
    for _, _ in pairs(t) do
        len = len + 1
    end
    return len
end

function Common.fileRead(path)
    if not path then return nil end
    local file = io.open(path, "r")
    if file then
        local data = file:read("*a")
        file:close()
        return data
    end
    return ""
end

function Common.fileWrite(path, cont)
    if not path then return nil end
    local file = io.open(path, "w")
    if file then
        file:write(cont)
        file:close()
        return true
    end
    return false
end

function Common.fileAppend(path, cont)
    if not path then return nil end
    local file = io.open(path, "a")
    if file then
        file:write(cont)
        file:close()
        return true
    end
    return false
end

function Common.sendPopen(cmd)
    local rsp = io.popen(cmd)
    if rsp then
        local result = rsp:read("*a")
        rsp:close()
        return result
    end
    return ""
end

function Common.sendTerminal(cmd)
    return ""
end

function Common.dump(o)
    if type(o) == 'table' then
        local s = '{ '
        for k, v in pairs(o) do
            if type(k) ~= 'number' then k = '"' .. k .. '"' end
            s = s .. '[' .. k .. '] = ' .. Common.dump(v) .. ','
        end
        return s .. '} '
    else
        return tostring(o)
    end
end

function Common.map(func, t)
    local ret = {}
    for i = 1, #t, 1 do
        ret[i] = func(t[i])
    end
    return ret
end

function Common.hasVal(valueArr, valueStr)
    for _, value in ipairs(valueArr) do
        if value == valueStr then
            return true
        end
    end
    return false
end

function Common.toLuaTable(enumerableObjs)
    local tmpTable = {}
    for key, value in pairs(enumerableObjs) do
        if type(key) == "number" then
            tmpTable[key + 1] = value
        else
            tmpTable[key] = value
        end
    end
    return tmpTable
end

return Common
