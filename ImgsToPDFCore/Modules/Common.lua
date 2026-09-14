local Common = {}

function Common.isInt(i)
    return math.type(i) == "integer" or (type(i) == "number" and i % 1 == 0)
end

function Common.isFloat(i)
    return math.type(i) == "float" and i % 1 ~= 0
end

function Common.isEmpty(o)
    if type(o) == "table" then
        return next(o) == nil
    end
    return o == nil or o == ''
end

function Common.len(t)
    if type(t) == "table" then
        local len = 0
        for _ in pairs(t) do
            len = len + 1
        end
        return len
    elseif type(t) == "string" then
        return #t
    end
    return 0
end

function Common.fileRead(path)
    if not path then return nil, "path is nil" end
    local file, err = io.open(path, "r")
    if file then
        local data = file:read("*a")
        file:close()
        return data
    end
    return nil, err
end

function Common.fileWrite(path, cont)
    if not path then return false, "path is nil" end
    local file, err = io.open(path, "w")
    if file then
        file:write(cont or "")
        file:close()
        return true
    end
    return false, err
end

function Common.fileAppend(path, cont)
    if not path then return false, "path is nil" end
    local file, err = io.open(path, "a")
    if file then
        file:write(cont or "")
        file:close()
        return true
    end
    return false, err
end

function Common.quote_arg(argument)
    if type(argument) == "table" then
        local r = {}
        for i, arg in ipairs(argument) do
            r[i] = Common.quote_arg(arg)
        end

        return table.concat(r, " ")
    end

    if package.config:sub(1,1) == '\\' then
        if argument == "" or argument:find('[ \f\t\v]') then
            argument = '"' .. argument:gsub([[(\*)"]], [[%1%1\"]]):gsub([[\+$]], "%0%0") .. '"'
        end
        return (argument:gsub('["^<>!|&%%]', "^%0"))
    else
        if argument == "" or argument:find('[^a-zA-Z0-9_@%+=:,./-]') then
            argument = "'" .. argument:gsub("'", [['\'']]) .. "'"
        end

        return argument
    end
end

function Common.sendPopen(cmd)
    local rsp, err = io.popen(cmd)
    if rsp then
        local result = rsp:read("*a")
        local ok, exit_type, code = rsp:close()
        return result, ok, code
    end
    return "", false, err
end

function Common.sendTerminal(cmd)
    local outfile = os.tmpname()
    local errfile = os.tmpname()

    cmd = cmd .. " > " .. Common.quote_arg(outfile) .. " 2> " .. Common.quote_arg(errfile)

    local status = os.execute(cmd)
    local outcontent = Common.fileRead(outfile) or ""
    local errcontent = Common.fileRead(errfile) or ""
    os.remove(outfile)
    os.remove(errfile)
    return status, outcontent, errcontent
end

function Common.dump(o, visited)
    visited = visited or {}
    local t = type(o)
    if t == "string" then
        return string.format("%q", o)
    elseif t ~= "table" then
        return tostring(o)
    end

    if visited[o] then
        return "<circular reference>"
    end
    visited[o] = true

    local parts = {}
    for k, v in pairs(o) do
        local k_str
        if type(k) == "number" then
            k_str = "[" .. k .. "]"
        elseif type(k) == "string" then
            k_str = "[" .. string.format("%q", k) .. "]"
        else
            k_str = "[" .. tostring(k) .. "]"
        end
        table.insert(parts, k_str .. " = " .. Common.dump(v, visited))
    end
    visited[o] = nil

    return "{ " .. table.concat(parts, ", ") .. " }"
end

function Common.map(func, t)
    local n = #t
    local ret = (table.create and table.create(n, 0)) or {}
    for i = 1, n do
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
