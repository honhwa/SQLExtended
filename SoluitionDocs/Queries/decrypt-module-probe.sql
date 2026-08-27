/*
    Encrypted-module decryption probe
    =================================

    Does in pure T-SQL exactly what SQLExtended/Decryption/ModuleDecryptionService.cs does in C#, so the
    two questions can be separated:

        1. Does the technique work on THIS instance?      <- this script answers it
        2. Is the extension's plumbing right?             <- only worth asking if (1) is yes

    HOW TO RUN
    ----------
    This must run over a Dedicated Administrator Connection. sys.sysobjvalues is not visible on a normal
    connection, and the script will tell you so rather than fail obscurely.

      File > New > Database Engine Query
      Server name:  ADMIN:YourServerName        (or ADMIN:YourServer\Instance)
      -> Connect

    SSMS will not give a DAC window IntelliSense, and it will refuse if another DAC is already open. Only
    one DAC exists per instance — that includes one this extension may still be holding.

    Then set the two variables below and run the whole script.

    WHAT IT DOES TO YOUR DATABASE
    -----------------------------
    It ALTERs the object to a throwaway definition and rolls that back in the same batch. Nothing is left
    changed. The ALTER does take a schema-modification lock, which is why LOCK_TIMEOUT is set — if the
    module is executing right now this gives up rather than blocking it.
*/

SET NOCOUNT ON;

------------------------------------------------------------------------------------------------------
-- Set these two.
------------------------------------------------------------------------------------------------------
DECLARE @Database  sysname = N'YourDatabase';
DECLARE @Object    nvarchar(776) = N'[dbo].[SecretProc]';   -- bracket-quoted, schema included
------------------------------------------------------------------------------------------------------

PRINT '=== Step 0: connection ===';

IF @Database <> DB_NAME()
BEGIN
    PRINT 'FAIL: run this with the query window pointed at [' + @Database + '] (current: [' + DB_NAME() + ']).';
    PRINT '      Change the database in the toolbar, or edit @Database. A DAC window cannot USE another database.';
    RETURN;
END;

PRINT '  Server:   ' + CONVERT(nvarchar(128), SERVERPROPERTY('ServerName'));
PRINT '  Database: ' + DB_NAME();
PRINT '  Login:    ' + SUSER_SNAME() + '   sysadmin: ' + CONVERT(varchar(1), ISNULL(IS_SRVROLEMEMBER('sysadmin'), 0));

BEGIN TRY
    DECLARE @dacProbe int = (SELECT TOP (1) 1 FROM sys.sysobjvalues WHERE valclass = 1);
    PRINT '  DAC:      yes (sys.sysobjvalues is readable)';
END TRY
BEGIN CATCH
    PRINT '  DAC:      NO';
    PRINT 'FAIL: sys.sysobjvalues is not readable, so this is not a dedicated administrator connection.';
    PRINT '      Reconnect with the server name prefixed "ADMIN:". A remote instance also needs';
    PRINT '      sp_configure ''remote admin connections'', 1. Azure SQL Database has no DAC at all.';
    RETURN;
END CATCH;

PRINT '';
PRINT '=== Step 1: find the object and its ciphertext ===';

DECLARE @objid int = OBJECT_ID(@Object);
IF @objid IS NULL
BEGIN
    PRINT 'FAIL: OBJECT_ID(''' + @Object + ''') returned NULL in [' + DB_NAME() + '].';
    RETURN;
END;

DECLARE @type char(2) = (SELECT [type] FROM sys.objects WHERE object_id = @objid);
PRINT '  object_id: ' + CONVERT(varchar(20), @objid) + '   type: ' + @type;

IF EXISTS (SELECT 1 FROM sys.sql_modules WHERE object_id = @objid AND [definition] IS NOT NULL)
BEGIN
    PRINT 'STOP: this module is NOT encrypted — sys.sql_modules already returns its text.';
    RETURN;
END;

-- The rows are concatenated in valnum order rather than assumed to be one, exactly as the C# does.
DECLARE @origCipher varbinary(max) = 0x;
SELECT @origCipher = @origCipher + CONVERT(varbinary(max), imageval)
FROM   sys.sysobjvalues
WHERE  valclass = 1 AND objid = @objid AND subobjid = 1
ORDER  BY valnum;

DECLARE @rows int = (SELECT COUNT(*) FROM sys.sysobjvalues WHERE valclass = 1 AND objid = @objid AND subobjid = 1);

IF DATALENGTH(@origCipher) = 0
BEGIN
    PRINT 'FAIL: no imageval rows found for valclass=1, subobjid=1. The storage layout is not what was assumed.';
    RETURN;
END;

DECLARE @chars int = DATALENGTH(@origCipher) / 2;
PRINT '  imageval rows: ' + CONVERT(varchar(10), @rows) + '   (more than 1 is worth reporting)';
PRINT '  ciphertext:    ' + CONVERT(varchar(20), DATALENGTH(@origCipher)) + ' bytes = ' + CONVERT(varchar(20), @chars) + ' characters';

PRINT '';
PRINT '=== Step 2: build the throwaway definition ===';

/*
    The body is built WITHOUT a leading keyword, because the statement is executed with one keyword and
    stored under another:

        executed:  ALTER  + body     -- the object exists, so it must be ALTERed
        stored:    CREATE + body     -- what the engine encrypts, and therefore what the XOR needs

    Getting this wrong is the single easiest mistake here and it is silent: ALTER is one character shorter
    than CREATE, so the recovered text is right for five characters and then one out of step for the rest
    of the module. It still comes back as a string.
*/
DECLARE @body nvarchar(max) =
    CASE @type
        WHEN 'P'  THEN N' PROCEDURE ' + @Object + N' WITH ENCRYPTION AS RETURN'
        WHEN 'V'  THEN N' VIEW '      + @Object + N' WITH ENCRYPTION AS SELECT 1 AS c'
        WHEN 'FN' THEN N' FUNCTION '  + @Object + N'() RETURNS int WITH ENCRYPTION AS BEGIN RETURN 1 END'
        WHEN 'IF' THEN N' FUNCTION '  + @Object + N'() RETURNS TABLE WITH ENCRYPTION AS RETURN SELECT 1 AS c'
        WHEN 'TF' THEN N' FUNCTION '  + @Object + N'() RETURNS @r TABLE (c int) WITH ENCRYPTION AS BEGIN RETURN END'
    END;

IF @body IS NULL
BEGIN
    PRINT 'FAIL: object type ''' + @type + ''' is not handled by this probe (triggers need their table named).';
    RETURN;
END;

-- Pad so the CREATE form — not the body, and not the ALTER form — reaches the original's length.
DECLARE @pad int = @chars - (LEN(N'CREATE') + LEN(@body));
SET @body = @body + CASE
                        WHEN @pad >= 3 THEN N' --' + REPLICATE(CONVERT(nvarchar(max), N'-'), @pad - 3)
                        WHEN @pad > 0  THEN REPLICATE(CONVERT(nvarchar(max), N' '), @pad)
                        ELSE N''
                    END;

DECLARE @dummy  nvarchar(max) = N'ALTER'  + @body;   -- executed
DECLARE @stored nvarchar(max) = N'CREATE' + @body;   -- the XOR mask

PRINT '  stored form:   ' + CONVERT(varchar(20), LEN(@stored)) + ' characters (target ' + CONVERT(varchar(20), @chars) + ')';
IF LEN(@stored) < @chars
    PRINT '  NOTE: the dummy is shorter than the original, so only the first ' + CONVERT(varchar(20), LEN(@stored)) + ' characters can be recovered.';

PRINT '';
PRINT '=== Step 3: ALTER inside a transaction, re-read, roll back ===';

DECLARE @dummyCipher varbinary(max) = 0x;
DECLARE @altered bit = 0;

SET XACT_ABORT ON;
SET LOCK_TIMEOUT 5000;   -- never block a module that is currently executing

BEGIN TRY
    BEGIN TRAN;

    EXEC sp_executesql @dummy;
    SET @altered = 1;

    SELECT @dummyCipher = @dummyCipher + CONVERT(varbinary(max), imageval)
    FROM   sys.sysobjvalues
    WHERE  valclass = 1 AND objid = @objid AND subobjid = 1
    ORDER  BY valnum;

    ROLLBACK;
    PRINT '  ALTER applied and rolled back. Second ciphertext: ' + CONVERT(varchar(20), DATALENGTH(@dummyCipher)) + ' bytes';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    PRINT 'FAIL at the ' + CASE WHEN @altered = 1 THEN 're-read' ELSE 'ALTER' END + ' step:';
    PRINT '  ' + ERROR_MESSAGE() + '  (error ' + CONVERT(varchar(10), ERROR_NUMBER()) + ')';
    PRINT '  The object is unchanged — the transaction was rolled back.';
    RETURN;
END CATCH;

-- Belt and braces: prove the rollback took.
IF NOT EXISTS (SELECT 1 FROM sys.sysobjvalues WHERE valclass = 1 AND objid = @objid AND subobjid = 1
               AND CONVERT(varbinary(max), imageval) = SUBSTRING(@origCipher, 1, 8000))
   AND @rows = 1
    PRINT '  WARNING: the stored ciphertext does not match what was read first. Check the object.';

PRINT '';
PRINT '=== Step 4: XOR the three together ===';

DECLARE @a nvarchar(max) = CONVERT(nvarchar(max), @origCipher);    -- reinterpret the bytes as UTF-16LE
DECLARE @b nvarchar(max) = CONVERT(nvarchar(max), @dummyCipher);
DECLARE @n int = @chars;

IF DATALENGTH(@b) / 2 < @n OR LEN(@stored) < @n
BEGIN
    SET @n = CASE WHEN DATALENGTH(@b) / 2 < LEN(@stored) THEN DATALENGTH(@b) / 2 ELSE LEN(@stored) END;
    PRINT '  Only ' + CONVERT(varchar(20), @n) + ' of ' + CONVERT(varchar(20), @chars) + ' characters can be recovered.';
END;

DECLARE @plain nvarchar(max) = N'';
DECLARE @i int = 1;

-- @stored, not @dummy: the CREATE form is what was encrypted.
WHILE @i <= @n
BEGIN
    SET @plain = @plain + NCHAR(UNICODE(SUBSTRING(@a, @i, 1)) ^ UNICODE(SUBSTRING(@b, @i, 1)) ^ UNICODE(SUBSTRING(@stored, @i, 1)));
    SET @i += 1;
END;

PRINT '';
PRINT '=== Result ===';
PRINT '';

-- PRINT truncates at 4000 nchars; chunk it so a long procedure is readable in the Messages tab.
DECLARE @p int = 1;
WHILE @p <= LEN(@plain)
BEGIN
    PRINT SUBSTRING(@plain, @p, 4000);
    SET @p += 4000;
END;

PRINT '';
IF @plain LIKE N'CREATE%' OR @plain LIKE N'ALTER%'
    PRINT '>>> SUCCESS: the recovered text opens with CREATE/ALTER, so the technique works on this instance.';
ELSE
    PRINT '>>> FAILED: the recovered text is not a module definition. Copy the output above into the issue.';

-- Also returned as a grid, which is easier to copy out of than the Messages tab.
SELECT RecoveredDefinition = @plain, Characters = @n, ObjectType = @type, ImagevalRows = @rows;
