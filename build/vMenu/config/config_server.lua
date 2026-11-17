Config = {
    EnableServerList = GetConvarBool("vmenu_disable_server_info_convars", false) == false, -- If you want to show the framework version in your server list info convars.
    DisableAI = GetConvarBool("vmenu_disable_ai", false), -- This will disable NPC spawning in bucket 0 (if you don't touch buckets, you don't need to worry about this). This is useful for people who want a quick and easy way to disable AI.

    -- Department Outfits (setr vmenu_dept_outfits true; server/deptOutfits.lua): the departments
    -- outfits can be published for, in menu order. Each id is the org id from PoliceEMSActivity's
    -- Config.Orgs, the same one the vMenu.DeptOutfits.Manage.<id> ace uses. A player on duty with an
    -- org that isn't listed can still wear its outfits (named by PoliceEMSActivity's label).
    -- member is the org's "permission" in PoliceEMSActivity's Config.Orgs (who may /duty as it):
    -- vMenu.DeptOutfits.ManageOwn manages exactly the orgs whose member ace the player has.
    DepartmentOutfitOrgs = {
        { id = "lspd", label = "Los Santos Police Department", member = "group.lspdblip" },
        { id = "bcso", label = "Blaine County Sheriff's Office", member = "group.bcsoblip" },
        { id = "sast", label = "San Andreas State Troopers", member = "group.sastblip" },
        { id = "sahp", label = "San Andreas Highway Patrol", member = "group.sahpblip" },
        { id = "fib", label = "Federal Investigation Bureau", member = "group.fibblip" },
        { id = "sasm", label = "San Andreas State Marshal", member = "group.sasmblip" },
        { id = "hsb", label = "Homeland Security", member = "group.hsbblip" },
        { id = "pbpd", label = "Paleto Bay Police Department", member = "group.pbpdblip" },
        { id = "nationalguard", label = "National Guard", member = "group.nationalguardblip" },
        { id = "dispatch", label = "Dispatch Certified", member = "group.dispatchblip" },
        { id = "pubcop", label = "Public Cop", member = "group.pubcopblip" },
        { id = "deptcoord", label = "Department Coordinator", member = "group.deptcoordblip" },
        { id = "fire", label = "San Andreas Fire & Rescue", member = "group.fireblip" },
        { id = "boxing", label = "Boxing", member = "group.boxingblip" },
        { id = "saa", label = "SAA", member = "group.saablip" },
        { id = "saaf", label = "SAA Air Force", member = "group.saafblip" },
        { id = "sadot", label = "SADOT", member = "group.sadotblip" },
        { id = "CertCivt2", label = "CertCivt2", member = "group.CertCivt2" },
        { id = "EliteCertCiv", label = "EliteCertCiv", member = "group.EliteCertCiv" },
        { id = "2100", label = "2100 Shoota's", member = "group.2100blip" },
        { id = "aod", label = "Angels Of Death", member = "group.aodblip" },
        { id = "s3", label = "South Side Shooters", member = "group.s3blip" },
        { id = "rm", label = "Romano Mafia", member = "group.rmblip" },
        { id = "42s", label = "42 Sinners", member = "group.42sblip" },
        { id = "lmc", label = "The Lost MC", member = "group.lmcblip" },
        { id = "mdz", label = "Madrazo Cartel", member = "group.mdzblip" },
        { id = "1800", label = "1800 Gunnaz", member = "group.1800blip" },
        { id = "esf", label = "East Side Family", member = "group.esfblip" },
        { id = "38", label = "38 Switchaz", member = "group.38blip" },
        { id = "tc", label = "The Collective", member = "group.tcblip" },
        { id = "dmc", label = "Desperados MC", member = "group.dmcblip" },
        { id = "wsm", label = "Western State Militia", member = "group.wsmblip" },
        { id = "odf", label = "O District Family", member = "group.odfblip" },
    },
}