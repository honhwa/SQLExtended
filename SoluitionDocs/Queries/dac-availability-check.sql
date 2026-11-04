-- ============================================================================================
--  Can this instance give us a Dedicated Administrator Connection?
--
--  Run this over an ORDINARY connection, as sysadmin, on the server whose encrypted modules will
--  not decrypt. Decryption of WITH ENCRYPTION module text has no second route: the ciphertext lives
--  in sys.sysobjvalues, that object is invisible over a normal connection even to sysadmin
--  (verified: Msg 208), and only the DAC can see it. So when decryption fails with
--
--      error 43 - An error occurred while obtaining the dedicated administrator connection (DAC) port
--
--  the question is not "what is wrong with SQLExtended", it is "why can the client not find the DAC",
--  and this script answers that from the instance itself rather than from the error text.
--
--  Output is one row per fact, then a verdict. Nothing here changes anything.
-- ============================================================================================

SET NOCOUNT ON;

DECLARE @edition     int = CAST(SERVERPROPERTY('EngineEdition') AS int);
DECLARE @remoteAdmin int = (SELECT CAST(value_in_use AS int) FROM sys.configurations WHERE name = 'remote admin connections');
DECLARE @dacSessions int = (SELECT COUNT(*)
                            FROM sys.dm_exec_sessions s
                            JOIN sys.endpoints e ON s.endpoint_id = e.endpoint_id
                            WHERE e.name = 'Dedicated Admin Connection' AND s.session_id <> @@SPID);

-- The DAC is TCP-only, so an instance with the TCP protocol switched off cannot have one.
DECLARE @tcpEnabled int = (SELECT TOP (1) CAST(value_data AS int)
                           FROM sys.dm_server_registry
                           WHERE registry_key LIKE '%SuperSocketNetLib\Tcp'
                             AND value_name = 'Enabled');

-- Every TDS listener, DAC included. This view does NOT say which is which - a DAC endpoint's type_desc
-- is 'TSQL', exactly like the normal endpoint - and sys.tcp_endpoints.port reports 0 for a dynamically
-- assigned DAC port even while it is listening. Measured on SQL Server 2019 Express with the DAC up.
DECLARE @listeners nvarchar(200) = STUFF((SELECT DISTINCT ', ' + CAST(port AS nvarchar(10))
                                          FROM sys.dm_tcp_listener_states
                                          WHERE state_desc = 'ONLINE'
                                          ORDER BY ', ' + CAST(port AS nvarchar(10))
                                          FOR XML PATH('')), 1, 2, '');

-- The authoritative source for the DAC's port: the ERRORLOG line written when the listener starts.
-- "Dedicated admin connection support was established for listening remotely on port 11618."
-- The text is localized, so a non-English instance yields nothing here - that is reported, not guessed.
DECLARE @dacLog nvarchar(max) = NULL;
BEGIN TRY
    CREATE TABLE #log (LogDate datetime, ProcessInfo nvarchar(64), Txt nvarchar(max));
    INSERT #log EXEC xp_readerrorlog 0, 1, N'Dedicated admin connection';
    SET @dacLog = (SELECT TOP (1) Txt FROM #log WHERE Txt LIKE N'%port%' ORDER BY LogDate DESC);
    DROP TABLE #log;
END TRY
BEGIN CATCH
    IF OBJECT_ID('tempdb..#log') IS NOT NULL DROP TABLE #log;
END CATCH;

DECLARE @dacPort int = NULL;
IF @dacLog IS NOT NULL
BEGIN
    -- The first number in the line, not the text after "port": "support was established" contains "port "
    -- too, so a PATINDEX on that phrase finds the wrong place and yields an empty string (and therefore 0).
    -- The message holds exactly one number, the port, so the first run of digits is it.
    DECLARE @start int = PATINDEX(N'%[0-9]%', @dacLog);
    IF @start > 0
    BEGIN
        DECLARE @num nvarchar(20) = SUBSTRING(@dacLog, @start, 20);
        SET @dacPort = TRY_CAST(LEFT(@num, PATINDEX(N'%[^0-9]%', @num + N'x') - 1) AS int);
    END;
END;

SELECT 'EngineEdition' AS fact,
       CAST(@edition AS nvarchar(16)) AS value,
       CASE @edition
            WHEN 4 THEN 'SQL Server Express'
            WHEN 5 THEN 'Azure SQL Database'
            WHEN 8 THEN 'Azure SQL Managed Instance'
            ELSE 'full SQL Server edition' END AS meaning
UNION ALL SELECT 'remote admin connections', CAST(@remoteAdmin AS nvarchar(16)),
       CASE WHEN @remoteAdmin = 1 THEN 'the DAC accepts remote clients'
            ELSE 'the DAC listens on the loopback address only' END
UNION ALL SELECT 'TCP protocol', ISNULL(CAST(@tcpEnabled AS nvarchar(16)), '(unknown)'),
       CASE WHEN @tcpEnabled = 0 THEN 'TCP/IP is disabled; the DAC has no protocol to listen on'
            WHEN @tcpEnabled = 1 THEN 'TCP/IP is enabled'
            ELSE 'not reported by this build' END
UNION ALL SELECT 'TCP listeners', ISNULL(@listeners, '(none)'),
       'ports the instance answers on; one of them is the DAC, and only connecting tells you which'
UNION ALL SELECT 'DAC port (ERRORLOG)', ISNULL(CAST(@dacPort AS nvarchar(16)), '(no line found)'),
       CASE WHEN @dacPort IS NULL THEN 'the listener never started, or the ERRORLOG is not in English'
            ELSE 'the port the DAC listener bound at startup' END
UNION ALL SELECT 'DAC sessions now', CAST(@dacSessions AS nvarchar(16)),
       CASE WHEN @dacSessions > 0 THEN 'the single DAC slot is in use - a second connection is refused'
            ELSE 'the slot is free' END;

-- ------------------------------------------------------------------------------------------------
--  Verdict. One line, saying what to do rather than what is broken.
-- ------------------------------------------------------------------------------------------------
DECLARE @verdict nvarchar(2000);

IF @dacPort IS NOT NULL
    SET @verdict = N'The DAC is listening on port ' + CAST(@dacPort AS nvarchar(16))
                 + N'. A client that cannot get that port from SQL Browser can still reach it by address: '
                 + N'tcp:<server>,' + CAST(@dacPort AS nvarchar(16))
                 + N' - that is what SQLExtended falls back to, so decryption should work as it stands.';
ELSE IF @edition = 4
    SET @verdict = N'Express does not start the DAC listener unless the service is started with trace flag 7806. '
                 + N'Add -T7806 to the startup parameters (SQL Server Configuration Manager > SQL Server Services > '
                 + N'right-click the instance > Properties > Startup Parameters) and restart the instance.';
ELSE IF @edition = 5
    SET @verdict = N'Azure SQL Database accepts a DAC only into a user database, never into master. Connect the '
                 + N'decryption to a user database and try again.';
ELSE IF @tcpEnabled = 0
    SET @verdict = N'No DAC listener and TCP/IP is off. The DAC is TCP-only: enable the protocol for this instance '
                 + N'and restart it.';
ELSE IF @dacSessions > 0
    SET @verdict = N'The single DAC slot is taken. Close the other ADMIN: session, or wait for the tool polling it.';
ELSE
    SET @verdict = N'No DAC listener. Check the instance ERRORLOG for "Dedicated admin connection support was not '
                 + N'started ..." - it states the reason. On a cluster the DAC is off by default.';

SELECT @verdict AS verdict;

-- How to connect by hand, using the form that is actually valid: a port can only be given to a plain TCP
-- address, never to the ADMIN: prefix (SqlClient rejects that with provider error 25).
IF @dacPort IS NOT NULL
    SELECT N'tcp:' + CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) + N',' + CAST(@dacPort AS nvarchar(16))
           AS dac_connection_string;
