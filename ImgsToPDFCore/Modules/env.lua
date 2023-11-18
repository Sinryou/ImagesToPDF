local env = {}

env.IS_WINDOWS = os.getenv("os") == "Windows_NT"
env.OS = env.IS_WINDOWS and "Windows" or "Unix"
env.HOME = env.IS_WINDOWS and os.getenv("userprofile") or os.getenv("home")
env.DESKTOP = env.HOME .. "/Desktop"

return env
