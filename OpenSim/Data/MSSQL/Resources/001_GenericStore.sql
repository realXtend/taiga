CREATE TABLE [generic] (
  [scope] [varchar](255) NOT NULL,
  [key] [varchar](255) NOT NULL,
  [value] [varchar](65535) NOT NULL,
  PRIMARY KEY CLUSTERED 
  (
	[scope],
	[key]
  ) WITH (PAD_INDEX  = OFF, STATISTICS_NORECOMPUTE  = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS  = ON, ALLOW_PAGE_LOCKS  = ON) ON [PRIMARY]
) ON [PRIMARY]
