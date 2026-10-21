-- No lib.versionCheck: it checked the upstream Gravxd/vMenu-ox fork, which is not this build.
CreateThread(function()
    if Config.EnableServerList then
        local currentVersion = GetResourceMetadata(GetCurrentResourceName(), "version", 0)
        SetConvarServerInfo("Framework", ("vMenu-ox - %s"):format(currentVersion))
        lib.print.info("vMenu-ox loaded! Version: " .. currentVersion)
    end
end)