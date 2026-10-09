local Common = {}

-- Lua 5.5 table.create 兼容层
if not table.create then
    rawset(table, "create", function(narr, nrec) return {} end)
end

function Common.isInt(i)
    return math.type(i) == "integer" or (type(i) == "number" and i % 1 == 0)
end

function Common.isFloat(i)
    return math.type(i) == "float" and i % 1 ~= 0
end

function Common.isEmpty(o)
    if o == nil or o == "" then
        return true
    end
    if type(o) == "table" then
        return next(o) == nil
    end
    return false
end

function Common.len(t)
    local tt <const> = type(t)
    if tt == "table" then
        local len = 0
        for _ in pairs(t) do
            len = len + 1
        end
        return len
    elseif tt == "string" then
        -- 优先使用 Lua 5.4/5.5 utf8.len，针对非法 utf-8 序列安全回退为字节长度 #t
        return utf8.len(t) or #t
    end
    return 0
end

function Common.fileRead(path)
    if not path then return nil, "path is nil" end
    local file <close>, err = io.open(path, "r")
    if file then
        return file:read("a")
    end
    return nil, err
end

function Common.fileWrite(path, cont)
    if not path then return false, "path is nil" end
    local file <close>, err = io.open(path, "w")
    if file then
        file:write(cont or "")
        return true
    end
    return false, err
end

function Common.fileAppend(path, cont)
    if not path then return false, "path is nil" end
    local file <close>, err = io.open(path, "a")
    if file then
        file:write(cont or "")
        return true
    end
    return false, err
end

function Common.quote_arg(argument)
    if type(argument) == "table" then
        local count <const> = #argument
        local r <const> = table.create(count, 0)
        for i = 1, count do
            r[i] = Common.quote_arg(argument[i])
        end
        return table.concat(r, " ")
    end

    local arg_str = tostring(argument)
    if package.config:sub(1, 1) == "\\" then
        if arg_str == "" or arg_str:find("[ \f\t\v]") then
            arg_str = '"' .. arg_str:gsub([[(\*)"]], [[%1%1\"]]):gsub([[\+$]], "%0%0") .. '"'
        end
        return (arg_str:gsub('["^<>!|&%%]', "^%0"))
    else
        if arg_str == "" or arg_str:find("[^a-zA-Z0-9_@%+=:,./-]") then
            arg_str = "'" .. arg_str:gsub("'", [['\'']]) .. "'"
        end
        return arg_str
    end
end

function Common.sendPopen(cmd)
    local rsp <close>, err = io.popen(cmd)
    if rsp then
        local result <const> = rsp:read("a")
        local ok, exit_type, code = rsp:close()
        return result, ok, code
    end
    return "", false, err
end

function Common.sendTerminal(cmd)
    local outfile <const> = os.tmpname()
    local errfile <const> = os.tmpname()

    -- Lua 5.4/5.5 <close> 机制保证临时文件退出作用域时自动清理
    local cleaner <close> = setmetatable({}, {
        __close = function()
            os.remove(outfile)
            os.remove(errfile)
        end
    })

    local full_cmd <const> = cmd .. " > " .. Common.quote_arg(outfile) .. " 2> " .. Common.quote_arg(errfile)
    local status = os.execute(full_cmd)
    local outcontent = Common.fileRead(outfile) or ""
    local errcontent = Common.fileRead(errfile) or ""
    return status, outcontent, errcontent
end

function Common.dump(o, visited)
    visited = visited or {}
    local t <const> = type(o)
    if t == "string" then
        return string.format("%q", o)
    elseif t ~= "table" then
        return tostring(o)
    end

    if visited[o] then
        return "<circular reference>"
    end
    visited[o] = true

    local parts <const> = table.create(8, 0)
    for k, v in pairs(o) do
        local k_str <const> = (type(k) == "number") and ("[" .. k .. "]")
            or (type(k) == "string") and ("[" .. string.format("%q", k) .. "]")
            or ("[" .. tostring(k) .. "]")
        table.insert(parts, k_str .. " = " .. Common.dump(v, visited))
    end
    visited[o] = nil

    return "{ " .. table.concat(parts, ", ") .. " }"
end

function Common.map(func, t)
    local n <const> = #t
    local ret <const> = table.create(n, 0)
    for i = 1, n do
        ret[i] = func(t[i])
    end
    return ret
end

function Common.hasVal(valueArr, valueStr)
    for i = 1, #valueArr do
        if valueArr[i] == valueStr then
            return true
        end
    end
    return false
end

function Common.toLuaTable(enumerableObjs)
    local tmpTable <const> = table.create(0, 8)
    for key, value in pairs(enumerableObjs) do
        if math.type(key) == "integer" or (type(key) == "number" and key % 1 == 0) then
            tmpTable[key + 1] = value
        else
            tmpTable[key] = value
        end
    end
    return tmpTable
end

return Common
