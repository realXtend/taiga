BEGIN;

CREATE TABLE `generic` (
    `scope` CHAR NOT NULL,
    `key` CHAR NOT NULL,
    `value` VARCHAR NOT NULL,
    PRIMARY KEY (`scope`, `key`)
);

COMMIT;