BEGIN;

CREATE TABLE `generic` (
  `scope` CHAR NOT NULL,
  `key` CHAR NOT NULL,
  `value` TEXT NOT NULL,
  PRIMARY KEY (`scope`, `key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Rev. 1';

COMMIT;