-- Outfit Code Table
CREATE TABLE IF NOT EXISTS `vmenu_outfits` (
    `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
    `discord_id` varchar(50) DEFAULT NULL,
    `data` longtext DEFAULT NULL,
    `created` datetime DEFAULT current_timestamp(),
    `label` varchar(50) DEFAULT NULL,
    PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB AUTO_INCREMENT=1 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC;

-- Vehicle Code Table
CREATE TABLE IF NOT EXISTS `vmenu_vehicles` (
    `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
    `discord_id` varchar(50) DEFAULT NULL,
    `data` longtext DEFAULT NULL,
    `created` datetime DEFAULT current_timestamp(),
    `label` varchar(50) DEFAULT NULL,
    PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB AUTO_INCREMENT=1 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC;

-- Save Backup Table (Misc Settings > Back Up My Saves). One row per Rockstar license.
CREATE TABLE IF NOT EXISTS `vmenu_backups` (
    `license` varchar(64) NOT NULL,
    `data` longtext NOT NULL,
    `entries` int(11) unsigned NOT NULL DEFAULT 0,
    `updated` datetime NOT NULL DEFAULT current_timestamp(),
    PRIMARY KEY (`license`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC;
