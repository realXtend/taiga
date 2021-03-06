BEGIN TRANSACTION;

CREATE TABLE estate_groups (
  EstateID int(10) NOT NULL,
  uuid char(36) NOT NULL
);

CREATE TABLE estate_managers (
  EstateID int(10) NOT NULL,
  uuid char(36) NOT NULL
);

CREATE TABLE estate_map (
  RegionID char(36) NOT NULL default '00000000-0000-0000-0000-000000000000',
  EstateID int(11) NOT NULL
);

CREATE TABLE estate_settings (
  EstateID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  EstateName varchar(64) default NULL,
  AbuseEmailToEstateOwner tinyint(4) NOT NULL,
  DenyAnonymous tinyint(4) NOT NULL,
  ResetHomeOnTeleport tinyint(4) NOT NULL,
  FixedSun tinyint(4) NOT NULL,
  DenyTransacted tinyint(4) NOT NULL,
  BlockDwell tinyint(4) NOT NULL,
  DenyIdentified tinyint(4) NOT NULL,
  AllowVoice tinyint(4) NOT NULL,
  UseGlobalTime tinyint(4) NOT NULL,
  PricePerMeter int(11) NOT NULL,
  TaxFree tinyint(4) NOT NULL,
  AllowDirectTeleport tinyint(4) NOT NULL,
  RedirectGridX int(11) NOT NULL,
  RedirectGridY int(11) NOT NULL,
  ParentEstateID int(10) NOT NULL,
  SunPosition double NOT NULL,
  EstateSkipScripts tinyint(4) NOT NULL,
  BillableFactor float NOT NULL,
  PublicAccess tinyint(4) NOT NULL
);
insert into estate_settings (EstateID,EstateName,AbuseEmailToEstateOwner,DenyAnonymous,ResetHomeOnTeleport,FixedSun,DenyTransacted,BlockDwell,DenyIdentified,AllowVoice,UseGlobalTime,PricePerMeter,TaxFree,AllowDirectTeleport,RedirectGridX,RedirectGridY,ParentEstateID,SunPosition,PublicAccess,EstateSkipScripts,BillableFactor) values ( 99, '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '');
delete from estate_settings;
CREATE TABLE estate_users (
  EstateID int(10) NOT NULL,
  uuid char(36) NOT NULL
);

CREATE TABLE estateban (
  EstateID int(10) NOT NULL,
  bannedUUID varchar(36) NOT NULL,
  bannedIp varchar(16) NOT NULL,
  bannedIpHostMask varchar(16) NOT NULL,
  bannedNameMask varchar(64) default NULL
);

drop table regionsettings;
CREATE TABLE regionsettings (
  regionUUID char(36) NOT NULL,
  block_terraform int(11) NOT NULL,
  block_fly int(11) NOT NULL,
  allow_damage int(11) NOT NULL,
  restrict_pushing int(11) NOT NULL,
  allow_land_resell int(11) NOT NULL,
  allow_land_join_divide int(11) NOT NULL,
  block_show_in_search int(11) NOT NULL,
  agent_limit int(11) NOT NULL,
  object_bonus float NOT NULL,
  maturity int(11) NOT NULL,
  disable_scripts int(11) NOT NULL,
  disable_collisions int(11) NOT NULL,
  disable_physics int(11) NOT NULL,
  terrain_texture_1 char(36) NOT NULL,
  terrain_texture_2 char(36) NOT NULL,
  terrain_texture_3 char(36) NOT NULL,
  terrain_texture_4 char(36) NOT NULL,
  elevation_1_nw float NOT NULL,
  elevation_2_nw float NOT NULL,
  elevation_1_ne float NOT NULL,
  elevation_2_ne float NOT NULL,
  elevation_1_se float NOT NULL,
  elevation_2_se float NOT NULL,
  elevation_1_sw float NOT NULL,
  elevation_2_sw float NOT NULL,
  water_height float NOT NULL,
  terrain_raise_limit float NOT NULL,
  terrain_lower_limit float NOT NULL,
  use_estate_sun int(11) NOT NULL,
  fixed_sun int(11) NOT NULL,
  sun_position float NOT NULL,
  covenant char(36) default NULL,
  Sandbox tinyint(4) NOT NULL,
  PRIMARY KEY  (regionUUID)
);

CREATE INDEX estate_ban_estate_id on estateban(EstateID);
CREATE INDEX estate_groups_estate_id on estate_groups(EstateID);
CREATE INDEX estate_managers_estate_id on estate_managers(EstateID);
CREATE INDEX estate_map_estate_id on estate_map(EstateID);
CREATE UNIQUE INDEX estate_map_region_id on estate_map(RegionID);
CREATE INDEX estate_users_estate_id on estate_users(EstateID);

COMMIT;