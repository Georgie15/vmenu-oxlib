Config = {
    EnableServerList = GetConvarBool("vmenu_disable_server_info_convars", false) == false, -- If you want to show the framework version in your server list info convars.
    DisableAI = GetConvarBool("vmenu_disable_ai", false), -- This will disable NPC spawning in bucket 0 (if you don't touch buckets, you don't need to worry about this). This is useful for people who want a quick and easy way to disable AI.

    -- Department Outfits (setr vmenu_dept_outfits true; server/deptOutfits.lua): the departments
    -- outfits can be published for, in menu order. Each id is the org id from PoliceEMSActivity's
    -- Config.Orgs, the same one the vMenu.DeptOutfits.Manage.<id> ace uses. A player on duty with an
    -- org that isn't listed can still wear its outfits (named by PoliceEMSActivity's label).
    DepartmentOutfitOrgs = {
        { id = "lspd", label = "Los Santos Police Department" },
        { id = "bcso", label = "Blaine County Sheriff's Office" },
        { id = "sast", label = "San Andreas State Troopers" },
        { id = "sahp", label = "San Andreas Highway Patrol" },
        { id = "fib", label = "Federal Investigation Bureau" },
        { id = "sasm", label = "San Andreas State Marshal" },
        { id = "hsb", label = "Homeland Security" },
        { id = "pbpd", label = "Paleto Bay Police Department" },
        { id = "nationalguard", label = "National Guard" },
        { id = "dispatch", label = "Dispatch Certified" },
        { id = "pubcop", label = "Public Cop" },
        { id = "deptcoord", label = "Department Coordinator" },
        { id = "fire", label = "San Andreas Fire & Rescue" },
        { id = "boxing", label = "Boxing" },
        { id = "saa", label = "SAA" },
        { id = "saaf", label = "SAA Air Force" },
        { id = "sadot", label = "SADOT" },
        { id = "CertCivt2", label = "CertCivt2" },
        { id = "EliteCertCiv", label = "EliteCertCiv" },
        { id = "2100", label = "2100 Shoota's" },
        { id = "aod", label = "Angels Of Death" },
        { id = "s3", label = "South Side Shooters" },
        { id = "rm", label = "Romano Mafia" },
        { id = "42s", label = "42 Sinners" },
        { id = "lmc", label = "The Lost MC" },
        { id = "mdz", label = "Madrazo Cartel" },
        { id = "1800", label = "1800 Gunnaz" },
        { id = "esf", label = "East Side Family" },
        { id = "38", label = "38 Switchaz" },
        { id = "tc", label = "The Collective" },
        { id = "dmc", label = "Desperados MC" },
        { id = "wsm", label = "Western State Militia" },
        { id = "odf", label = "O District Family" },
    },
}