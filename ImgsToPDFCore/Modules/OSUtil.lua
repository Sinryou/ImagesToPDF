local OSUtil = {}
local ffi = require("ffi")

OSUtil.OS = ffi.os
OSUtil.ARCH = ffi.arch
OSUtil.IS_WINDOWS = ffi.os == "Windows"
OSUtil.IS_UNIX_LIKE = not OSUtil.IS_WINDOWS
OSUtil.HOME = (OSUtil.IS_WINDOWS and os.getenv("userprofile")) or os.getenv("HOME") or "."
OSUtil.DESKTOP = OSUtil.HOME .. "/Desktop"
OSUtil.TMPDIR = (OSUtil.IS_WINDOWS and os.getenv("temp")) or os.getenv("TMPDIR") or os.getenv("TMP") or "/tmp"

if ffi.os == "Windows" then
    ffi.cdef[[
        void Sleep(unsigned long ms);
    ]]
else
    ffi.cdef[[
        struct pollfd {
            int fd;
            short events;
            short revents;
        };

        int poll(struct pollfd *fds, unsigned long nfds, int timeout);
    ]]
end

function OSUtil.sleep(s)
    if OSUtil.IS_WINDOWS then
        ffi.C.Sleep(s * 1000)
    else
        ffi.C.poll(nil, 0, s * 1000)
    end
end

return OSUtil
