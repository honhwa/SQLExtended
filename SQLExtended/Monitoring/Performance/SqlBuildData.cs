namespace SQLExtended.Monitoring.Performance;

/// <summary>
/// A snapshot of the SQL Server build list from <c>sqlserverbuilds.blogspot.com</c>, taken 2026-07-17:
/// 14 releases and 1340 builds.
///
/// <para><b>Generated — do not hand-edit.</b> Re-run
/// <c>SoluitionDocs/Tools/generate-sql-build-catalog.py</c> against the page to refresh it, which is also the
/// only thing that makes the data newer. The tab reads <see cref="SnapshotDate"/> and says so on screen, so a
/// stale answer is never presented as a current one.</para>
///
/// <para>One record per line so the whole thing is one string literal rather than 1340 object
/// initialisers — the C# compiler is markedly slower over the latter and the diff of a refresh is unreadable.
/// <c>R</c> starts a release and every <c>B</c> after it belongs to that release:</para>
///
/// <code>
/// R  key  name  codename  rtmBuild  released  mainstreamEnd  extendedEnd
/// B  build  label  kind  kb  released  withdrawn  description
/// </code>
///
/// <para>Parsed by <see cref="SqlBuildCatalog"/>. Tab-separated, and no field may contain a tab.</para>
/// </summary>
internal static class SqlBuildData
{
    /// <summary>When the page this was generated from was last modified.</summary>
    public const string SnapshotDate = "2026-07-17";

    /// <summary>Where it came from, shown on the tab so the numbers can be checked against the source.</summary>
    public const string SourceUrl = "https://sqlserverbuilds.blogspot.com/";

    public const string Catalog =
        "R	17.0	SQL Server 2025		17.0.1000.7	2025-11-18	2031-01-06	2036-01-06\n" +
        "B	17.0.4065.4	CU7	CumulativeUpdate	5096981	2026-07-16		Cumulative update 7 (CU7) for SQL Server 2025\n" +
        "B	17.0.4060.2	CU6 + security update	SecurityUpdate	5101346	2026-07-14		Security update for SQL Server 2025 CU6: July 14, 2026\n" +
        "B	17.0.4055.5	CU6	CumulativeUpdate	5093421	2026-06-17		Cumulative update 6 (CU6) for SQL Server 2025\n" +
        "B	17.0.4045.5	CU5	CumulativeUpdate	5084896	2026-05-20		Cumulative update 5 (CU5) for SQL Server 2025\n" +
        "B	17.0.4040.1	CU4 + security update	SecurityUpdate	5089899	2026-05-12		Security update for SQL Server 2025 CU4: May 12, 2026\n" +
        "B	17.0.4035.5	CU4	CumulativeUpdate	5081495	2026-04-16		Cumulative update 4 (CU4) for SQL Server 2025\n" +
        "B	17.0.4030.1	CU3 + security update	SecurityUpdate	5083245	2026-04-14		Security update for SQL Server 2025 CU3: April 14, 2026\n" +
        "B	17.0.4025.3	CU3	CumulativeUpdate	5077896	2026-03-12		Cumulative update 3 (CU3) for SQL Server 2025\n" +
        "B	17.0.4020.2	CU2 + security update	SecurityUpdate	5077466	2026-03-10		Security update for SQL Server 2025 CU2: March 10, 2026\n" +
        "B	17.0.4015.4	CU2	CumulativeUpdate	5075211	2026-02-12		Cumulative update 2 (CU2) for SQL Server 2025\n" +
        "B	17.0.4006.2	CU1	CumulativeUpdate	5078298	2026-01-29		Cumulative update 1 (CU1) for SQL Server 2025 (Rereleased)\n" +
        "B	17.0.4005.7	CU1	CumulativeUpdate		2026-01-15	1	Cumulative update 1 (CU1) for SQL Server 2025\n" +
        "B	17.0.1125.2	RTM + security update	SecurityUpdate	5102333	2026-07-14		Security update for SQL Server 2025 GDR: July 14, 2026\n" +
        "B	17.0.1115.1	RTM + security update	SecurityUpdate	5091223	2026-05-12		Security update for SQL Server 2025 GDR: May 12, 2026\n" +
        "B	17.0.1110.1	RTM + security update	SecurityUpdate	5084814	2026-04-14		Security update for SQL Server 2025 GDR: April 14, 2026\n" +
        "B	17.0.1105.2	RTM + security update	SecurityUpdate	5077468	2026-03-10		Security update for SQL Server 2025 GDR: March 10, 2026\n" +
        "B	17.0.1050.2	RTM + security update	SecurityUpdate	5073177	2026-01-13		Security update for SQL Server 2025 GDR: January 13, 2026\n" +
        "B	17.0.1000.7	RTM	Rtm		2025-11-18		Microsoft SQL Server 2025 RTM\n" +
        "B	17.0.925.4	RC1	Preview		2025-09-16		Microsoft SQL Server 2025 Release Candidate 1 (RC 1) Beta\n" +
        "B	17.0.900.7	RC0	Preview		2025-08-21		Microsoft SQL Server 2025 Release Candidate 0 (RC 0) Beta\n" +
        "B	17.0.800.3	CTP 2.1	Preview		2025-06-16		Microsoft SQL Server 2025 Public Preview (CTP 2.1) Beta\n" +
        "B	17.0.700.9	CTP 2.0	Preview		2025-05-19		Microsoft SQL Server 2025 Public Preview (CTP 2.0) Beta\n" +
        "B	17.0.600.9	CTP 1.5	Preview		2025-04-10		Microsoft SQL Server 2025 Community Technology Preview 1.5 (CTP 1.5) Beta\n" +
        "B	17.0.17.0	CTP 1.0	Preview		2024-11-19		Microsoft SQL Server 2025 Community Technology Preview 1.0 (CTP 1.0) Beta\n" +
        "R	16.0	SQL Server 2022	Dallas	16.0.1000.6	2022-11-16	2028-01-11	2033-01-11\n" +
        "B	16.0.4265.3	CU26	CumulativeUpdate	5093420	2026-07-16		Cumulative update 26 (CU26) for SQL Server 2022\n" +
        "B	16.0.4262.2	CU25 + security update	SecurityUpdate	5101347	2026-07-14		Security update for SQL Server 2022 CU25: July 14, 2026\n" +
        "B	16.0.4255.1	CU25	CumulativeUpdate	5081477	2026-05-20		Cumulative update 25 (CU25) for SQL Server 2022\n" +
        "B	16.0.4252.3	CU24 + security update	SecurityUpdate	5089900	2026-05-12		Security update for SQL Server 2022 CU24: May 12, 2026\n" +
        "B	16.0.4250.1	CU24 + security update	SecurityUpdate	5083252	2026-04-14		Security update for SQL Server 2022 CU24: April 14, 2026\n" +
        "B	16.0.4245.2	CU24	CumulativeUpdate	5080999	2026-03-12		Cumulative update 24 (CU24) for SQL Server 2022\n" +
        "B	16.0.4240.4	CU23 + security update	SecurityUpdate	5077464	2026-03-10		Security update for SQL Server 2022 CU23: March 10, 2026\n" +
        "B	16.0.4236.2	CU23	CumulativeUpdate	5078297	2026-01-29		Cumulative update 23 (CU23) for SQL Server 2022 (Rereleased)\n" +
        "B	16.0.4235.2	CU23	CumulativeUpdate	5074819	2026-01-15	1	Cumulative update 23 (CU23) for SQL Server 2022\n" +
        "B	16.0.4230.2	CU22 + security update	SecurityUpdate	5072936	2026-01-13		Security update for SQL Server 2022 CU22: January 13, 2026\n" +
        "B	16.0.4225.2	CU22	CumulativeUpdate	5068450	2025-11-13		Cumulative update 22 (CU22) for SQL Server 2022\n" +
        "B	16.0.4222.2	CU21 + security update	SecurityUpdate	5068406	2025-11-11		Security update for SQL Server 2022 CU21: November 11, 2025\n" +
        "B	16.0.4215.2	CU21	CumulativeUpdate	5065865	2025-09-11		Cumulative update 21 (CU21) for SQL Server 2022\n" +
        "B	16.0.4212.1	CU20 + security update	SecurityUpdate	5065220	2025-09-09		Security update for SQL Server 2022 CU20: September 9, 2025\n" +
        "B	16.0.4210.1	CU20 + security update	SecurityUpdate	5063814	2025-08-12		Security update for SQL Server 2022 CU20: August 12, 2025\n" +
        "B	16.0.4205.1	CU20	CumulativeUpdate	5059390	2025-07-10		Cumulative update 20 (CU20) for SQL Server 2022\n" +
        "B	16.0.4200.1	CU19 + security update	SecurityUpdate	5058721	2025-07-08		Security update for SQL Server 2022 CU19: July 8, 2025\n" +
        "B	16.0.4195.2	CU19	CumulativeUpdate	5054531	2025-05-15		Cumulative update 19 (CU19) for SQL Server 2022\n" +
        "B	16.0.4185.3	CU18	CumulativeUpdate	5050771	2025-03-13		Cumulative update 18 (CU18) for SQL Server 2022\n" +
        "B	16.0.4175.1	CU17	CumulativeUpdate	5048038	2025-01-16		Cumulative update 17 (CU17) for SQL Server 2022\n" +
        "B	16.0.4165.4	CU16	CumulativeUpdate	5048033	2024-11-14		Cumulative update 16 (CU16) for SQL Server 2022\n" +
        "B	16.0.4155.4	CU15 + security update	SecurityUpdate	5046862	2024-11-12		Security update for SQL Server 2022 CU15: November 12, 2024\n" +
        "B	16.0.4150.1	CU15 + security update	SecurityUpdate	5046059	2024-10-08		Security update for SQL Server 2022 CU15: October 8, 2024\n" +
        "B	16.0.4145.4	CU15	CumulativeUpdate	5041321	2024-09-25		Cumulative update 15 (CU15) for SQL Server 2022\n" +
        "B	16.0.4140.3	CU14 + security update	SecurityUpdate	5042578	2024-09-10		Security update for SQL Server 2022 CU14: September 10, 2024\n" +
        "B	16.0.4135.4	CU14	CumulativeUpdate	5038325	2024-07-23		Cumulative update 14 (CU14) for SQL Server 2022\n" +
        "B	16.0.4131.2	CU13 + security update	SecurityUpdate	5040939	2024-07-09		Security update for SQL Server 2022 CU13: July 9, 2024\n" +
        "B	16.0.4125.3	CU13	CumulativeUpdate	5036432	2024-05-16		Cumulative update 13 (CU13) for SQL Server 2022\n" +
        "B	16.0.4120.1	CU12 + security update	SecurityUpdate	5036343	2024-04-09		Security update for SQL Server 2022 CU12: April 9, 2024\n" +
        "B	16.0.4115.5	CU12	CumulativeUpdate	5033663	2024-03-14		Cumulative update 12 (CU12) for SQL Server 2022\n" +
        "B	16.0.4105.2	CU11	CumulativeUpdate	5032679	2024-01-11		Cumulative update 11 (CU11) for SQL Server 2022\n" +
        "B	16.0.4100.1	CU10 + security update	SecurityUpdate	5033592	2024-01-09		Security update for SQL Server 2022 CU10: January 9, 2024\n" +
        "B	16.0.4095.4	CU10	CumulativeUpdate	5031778	2023-11-16		Cumulative update 10 (CU10) for SQL Server 2022\n" +
        "B	16.0.4085.2	CU9	CumulativeUpdate	5030731	2023-10-12		Cumulative update 9 (CU9) for SQL Server 2022\n" +
        "B	16.0.4080.1	CU8 + security update	SecurityUpdate	5029503	2023-10-10		Security update for SQL Server 2022 CU8: October 10, 2023\n" +
        "B	16.0.4075.1	CU8	CumulativeUpdate	5029666	2023-09-14		Cumulative update 8 (CU8) for SQL Server 2022\n" +
        "B	16.0.4065.3	CU7	CumulativeUpdate	5028743	2023-08-10		Cumulative update 7 (CU7) for SQL Server 2022\n" +
        "B	16.0.4055.4	CU6	CumulativeUpdate	5027505	2023-07-13		Cumulative update 6 (CU6) for SQL Server 2022\n" +
        "B	16.0.4045.3	CU5	CumulativeUpdate	5026806	2023-06-15		Cumulative update 5 (CU5) for SQL Server 2022\n" +
        "B	16.0.4035.4	CU4	CumulativeUpdate	5026717	2023-05-11		Cumulative update 4 (CU4) for SQL Server 2022\n" +
        "B	16.0.4025.1	CU3	CumulativeUpdate	5024396	2023-04-13		Cumulative update 3 (CU3) for SQL Server 2022\n" +
        "B	16.0.4015.1	CU2	CumulativeUpdate	5023127	2023-03-15		Cumulative update 2 (CU2) for SQL Server 2022\n" +
        "B	16.0.4003.1	CU1	CumulativeUpdate	5022375	2023-02-16		Cumulative update 1 (CU1) for SQL Server 2022\n" +
        "B	16.0.1190.2	RTM + security update	SecurityUpdate	5102334	2026-07-14		Security update for SQL Server 2022 GDR: July 14, 2026\n" +
        "B	16.0.1180.1	RTM + security update	SecurityUpdate	5091158	2026-05-12		Security update for SQL Server 2022 GDR: May 12, 2026\n" +
        "B	16.0.1175.1	RTM + security update	SecurityUpdate	5084815	2026-04-14		Security update for SQL Server 2022 GDR: April 14, 2026\n" +
        "B	16.0.1170.5	RTM + security update	SecurityUpdate	5077465	2026-03-10		Security update for SQL Server 2022 GDR: March 10, 2026\n" +
        "B	16.0.1165.1	RTM + security update	SecurityUpdate	5073031	2026-01-13		Security update for SQL Server 2022 GDR: January 13, 2026\n" +
        "B	16.0.1160.1	RTM + security update	SecurityUpdate	5068407	2025-11-11		Security update for SQL Server 2022 GDR: November 11, 2025\n" +
        "B	16.0.1150.1	RTM + security update	SecurityUpdate	5065221	2025-09-09		Security update for SQL Server 2022 GDR: September 9, 2025\n" +
        "B	16.0.1145.1	RTM + security update	SecurityUpdate	5063756	2025-08-12		Security update for SQL Server 2022 GDR: August 12, 2025\n" +
        "B	16.0.1140.6	RTM + security update	SecurityUpdate	5058712	2025-07-08		Security update for SQL Server 2022 GDR: July 8, 2025\n" +
        "B	16.0.1135.2	RTM + security update	SecurityUpdate	5046861	2024-11-12		Security update for SQL Server 2022 GDR: November 12, 2024\n" +
        "B	16.0.1130.5	RTM + security update	SecurityUpdate	5046057	2024-10-08		Security update for SQL Server 2022 GDR: October 8, 2024\n" +
        "B	16.0.1125.1	RTM + security update	SecurityUpdate	5042211	2024-09-10		Security update for SQL Server 2022 GDR: September 10, 2024\n" +
        "B	16.0.1121.4	RTM + security update	SecurityUpdate	5040936	2024-07-09		Security update for SQL Server 2022 GDR: July 9, 2024\n" +
        "B	16.0.1115.1	RTM + security update	SecurityUpdate	5035432	2024-04-09		Security update for SQL Server 2022 GDR: April 9, 2024\n" +
        "B	16.0.1110.1	RTM + security update	SecurityUpdate	5032968	2024-01-09		Security update for SQL Server 2022 GDR: January 9, 2024\n" +
        "B	16.0.1105.1	RTM + security update	SecurityUpdate	5029379	2023-10-10		Security update for SQL Server 2022 GDR: October 10, 2023\n" +
        "B	16.0.1050.5	RTM + security update	SecurityUpdate	5021522	2023-02-14		Security update for SQL Server 2022 GDR: February 14, 2023\n" +
        "B	16.0.1000.6	RTM	Rtm		2022-11-16		Microsoft SQL Server 2022 RTM\n" +
        "B	16.0.950.9	RC1	Preview		2022-09-22		Microsoft SQL Server 2022 Release Candidate 1 (RC1) Beta\n" +
        "B	16.0.900.6	RC0	Preview		2022-08-23		Microsoft SQL Server 2022 Release Candidate 0 (RC0) Beta\n" +
        "B	16.0.700.4	CTP 2.1	Preview		2022-07-27		Microsoft SQL Server 2022 Community Technology Public Preview 2.1 (CTP 2.1) Beta\n" +
        "B	16.0.600.9	CTP 2.0	Preview		2022-05-20		Microsoft SQL Server 2022 Community Technology Public Preview 2.0 (CTP 2.0) Beta\n" +
        "B	16.0.500.2	CTP 1.5	Preview				Microsoft SQL Server 2022 Community Technology Preview 1.5 (CTP 1.5) Beta\n" +
        "B	16.0.400.2	CTP 1.4	Preview				Microsoft SQL Server 2022 Community Technology Preview 1.4 (CTP 1.4) Beta\n" +
        "B	16.0.300.4	CTP 1.3	Preview				Microsoft SQL Server 2022 Community Technology Preview 1.3 (CTP 1.3) Beta\n" +
        "B	16.0.200.2	CTP 1.2	Preview				Microsoft SQL Server 2022 Community Technology Preview 1.2 (CTP 1.2) Beta\n" +
        "B	16.0.101.1	CTP 1.1	Preview				Microsoft SQL Server 2022 Community Technology Preview 1.1 (CTP 1.1) Beta\n" +
        "B	16.0.100.4	CTP 1.0	Preview		2021-12-07		Microsoft SQL Server 2022 Community Technology Preview 1.0 (CTP 1.0) Beta\n" +
        "R	15.0	SQL Server 2019		15.0.2000.5	2019-11-04	2025-02-28	2030-01-08\n" +
        "B	15.0.4480.2	CU32 + security update	SecurityUpdate	5102335	2026-07-14		Security update for SQL Server 2019 CU32: July 14, 2026\n" +
        "B	15.0.4470.1	CU32 + security update	SecurityUpdate	5090407	2026-05-12		Security update for SQL Server 2019 CU32: May 12, 2026\n" +
        "B	15.0.4465.1	CU32 + security update	SecurityUpdate	5084816	2026-04-14		Security update for SQL Server 2019 CU32: April 14, 2026\n" +
        "B	15.0.4460.4	CU32 + security update	SecurityUpdate	5077469	2026-03-10		Security update for SQL Server 2019 CU32: March 10, 2026\n" +
        "B	15.0.4455.2	CU32 + security update	SecurityUpdate	5068404	2025-11-11		Security update for SQL Server 2019 CU32: November 11, 2025\n" +
        "B	15.0.4445.1	CU32 + security update	SecurityUpdate	5065222	2025-09-09		Security update for SQL Server 2019 CU32: September 9, 2025\n" +
        "B	15.0.4440.1	CU32 + security update	SecurityUpdate	5063757	2025-08-12		Security update for SQL Server 2019 CU32: August 12, 2025\n" +
        "B	15.0.4435.7	CU32 + security update	SecurityUpdate	5058722	2025-07-08		Security update for SQL Server 2019 CU32: July 8, 2025\n" +
        "B	15.0.4430.1	CU32	CumulativeUpdate	5054833	2025-02-27		Cumulative update 32 (CU32) for SQL Server 2019\n" +
        "B	15.0.4420.2	CU31	CumulativeUpdate	5049296	2025-02-13		Cumulative update 31 (CU31) for SQL Server 2019\n" +
        "B	15.0.4415.2	CU30	CumulativeUpdate	5049235	2024-12-12		Cumulative update 30 (CU30) for SQL Server 2019\n" +
        "B	15.0.4410.1	CU29 + security update	SecurityUpdate	5046860	2024-11-12		Security update for SQL Server 2019 CU29: November 12, 2024\n" +
        "B	15.0.4405.4	CU29	CumulativeUpdate	5046365	2024-10-31		Cumulative update 29 (CU29) for SQL Server 2019\n" +
        "B	15.0.4395.2	CU28 + security update	SecurityUpdate	5046060	2024-10-08		Security update for SQL Server 2019 CU28: October 8, 2024\n" +
        "B	15.0.4390.2	CU28 + security update	SecurityUpdate	5042749	2024-09-10		Security update for SQL Server 2019 CU28: September 10, 2024\n" +
        "B	15.0.4385.2	CU28	CumulativeUpdate	5039747	2024-08-01		Cumulative update 28 (CU28) for SQL Server 2019\n" +
        "B	15.0.4382.1	CU27 + security update	SecurityUpdate	5040948	2024-07-09		Security update for SQL Server 2019 CU27: July 9, 2024\n" +
        "B	15.0.4375.4	CU27	CumulativeUpdate	5037331	2024-06-13		Cumulative update 27 (CU27) for SQL Server 2019\n" +
        "B	15.0.4365.2	CU26	CumulativeUpdate	5035123	2024-04-11		Cumulative update 26 (CU26) for SQL Server 2019\n" +
        "B	15.0.4360.2	CU25 + security update	SecurityUpdate	5036335	2024-04-09		Security update for SQL Server 2019 CU25: April 9, 2024\n" +
        "B	15.0.4355.3	CU25	CumulativeUpdate	5033688	2024-02-15		Cumulative update 25 (CU25) for SQL Server 2019\n" +
        "B	15.0.4345.5	CU24	CumulativeUpdate	5031908	2023-12-14		Cumulative update 24 (CU24) for SQL Server 2019\n" +
        "B	15.0.4335.1	CU23	CumulativeUpdate	5030333	2023-10-12		Cumulative update 23 (CU23) for SQL Server 2019\n" +
        "B	15.0.4326.1	CU22 + security update	SecurityUpdate	5029378	2023-10-10		Security update for SQL Server 2019 CU22: October 10, 2023\n" +
        "B	15.0.4322.2	CU22	CumulativeUpdate	5027702	2023-08-14		Cumulative update 22 (CU22) for SQL Server 2019\n" +
        "B	15.0.4316.3	CU21	CumulativeUpdate	5025808	2023-06-15		Cumulative update 21 (CU21) for SQL Server 2019\n" +
        "B	15.0.4312.2	CU20	CumulativeUpdate	5024276	2023-04-13		Cumulative update 20 (CU20) for SQL Server 2019\n" +
        "B	15.0.4298.1	CU19	CumulativeUpdate	5023049	2023-02-16		Cumulative update 19 (CU19) for SQL Server 2019\n" +
        "B	15.0.4280.7	CU18 + security update	SecurityUpdate	5021124	2023-02-14		Security update for SQL Server 2019 CU18: February 14, 2023\n" +
        "B	15.0.4261.1	CU18	CumulativeUpdate	5017593	2022-09-28		Cumulative update 18 (CU18) for SQL Server 2019\n" +
        "B	15.0.4249.2	CU17	CumulativeUpdate	5016394	2022-08-11		Cumulative update 17 (CU17) for SQL Server 2019\n" +
        "B	15.0.4236.7	CU16 + security update	SecurityUpdate	5014353	2022-06-14		Security update for SQL Server 2019 CU16: June 14, 2022\n" +
        "B	15.0.4223.1	CU16	CumulativeUpdate	5011644	2022-04-18		Cumulative update 16 (CU16) for SQL Server 2019\n" +
        "B	15.0.4198.2	CU15	CumulativeUpdate	5008996	2022-01-27		Cumulative update 15 (CU15) for SQL Server 2019\n" +
        "B	15.0.4188.2	CU14	CumulativeUpdate	5007182	2021-11-22		Cumulative update 14 (CU14) for SQL Server 2019\n" +
        "B	15.0.4178.1	CU13	CumulativeUpdate	5005679	2021-10-05		Cumulative update 13 (CU13) for SQL Server 2019\n" +
        "B	15.0.4153.1	CU12	CumulativeUpdate	5004524	2021-08-04		Cumulative update 12 (CU12) for SQL Server 2019\n" +
        "B	15.0.4138.2	CU11	CumulativeUpdate	5003249	2021-06-10		Cumulative update 11 (CU11) for SQL Server 2019\n" +
        "B	15.0.4123.1	CU10	CumulativeUpdate	5001090	2021-04-06		Cumulative update 10 (CU10) for SQL Server 2019\n" +
        "B	15.0.4102.2	CU9	CumulativeUpdate	5000642	2021-02-11		Cumulative update 9 (CU9) for SQL Server 2019\n" +
        "B	15.0.4083.2	CU8 + security update	SecurityUpdate	4583459	2021-01-12		Security update for SQL Server 2019 CU8: January 12, 2021\n" +
        "B	15.0.4073.23	CU8	CumulativeUpdate	4577194	2020-10-01		Cumulative update 8 (CU8) for SQL Server 2019\n" +
        "B	15.0.4063.15	CU7	CumulativeUpdate	4570012	2020-09-02	1	Cumulative update 7 (CU7) for SQL Server 2019\n" +
        "B	15.0.4053.23	CU6	CumulativeUpdate	4563110	2020-08-04		Cumulative update 6 (CU6) for SQL Server 2019\n" +
        "B	15.0.4043.16	CU5	CumulativeUpdate	4552255	2020-06-22		Cumulative update 5 (CU5) for SQL Server 2019\n" +
        "B	15.0.4033.1	CU4	CumulativeUpdate	4548597	2020-03-31		Cumulative update 4 (CU4) for SQL Server 2019\n" +
        "B	15.0.4023.6	CU3	CumulativeUpdate	4538853	2020-03-12		Cumulative update 3 (CU3) for SQL Server 2019\n" +
        "B	15.0.4013.40	CU2	CumulativeUpdate	4536075	2020-02-13	1	Cumulative update 2 (CU2) for SQL Server 2019\n" +
        "B	15.0.4003.23	CU1	CumulativeUpdate	4527376	2020-01-07		Cumulative update 1 (CU1) for SQL Server 2019\n" +
        "B	15.0.2180.2	RTM + security update	SecurityUpdate	5102336	2026-07-14		Security update for SQL Server 2019 GDR: July 14, 2026\n" +
        "B	15.0.2170.1	RTM + security update	SecurityUpdate	5090408	2026-05-12		Security update for SQL Server 2019 GDR: May 12, 2026\n" +
        "B	15.0.2165.1	RTM + security update	SecurityUpdate	5084817	2026-04-14		Security update for SQL Server 2019 GDR: April 14, 2026\n" +
        "B	15.0.2160.4	RTM + security update	SecurityUpdate	5077470	2026-03-10		Security update for SQL Server 2019 GDR: March 10, 2026\n" +
        "B	15.0.2155.2	RTM + security update	SecurityUpdate	5068405	2025-11-11		Security update for SQL Server 2019 GDR: November 11, 2025\n" +
        "B	15.0.2145.1	RTM + security update	SecurityUpdate	5065223	2025-09-09		Security update for SQL Server 2019 GDR: September 9, 2025\n" +
        "B	15.0.2140.1	RTM + security update	SecurityUpdate	5063758	2025-08-12		Security update for SQL Server 2019 GDR: August 12, 2025\n" +
        "B	15.0.2135.5	RTM + security update	SecurityUpdate	5058713	2025-07-08		Security update for SQL Server 2019 GDR: July 8, 2025\n" +
        "B	15.0.2130.3	RTM + security update	SecurityUpdate	5046859	2024-11-12		Security update for SQL Server 2019 GDR: November 12, 2024\n" +
        "B	15.0.2125.1	RTM + security update	SecurityUpdate	5046056	2024-10-08		Security update for SQL Server 2019 GDR: October 8, 2024\n" +
        "B	15.0.2120.1	RTM + security update	SecurityUpdate	5042214	2024-09-10		Security update for SQL Server 2019 GDR: September 10, 2024\n" +
        "B	15.0.2116.2	RTM + security update	SecurityUpdate	5040986	2024-07-09		Security update for SQL Server 2019 GDR: July 9, 2024\n" +
        "B	15.0.2110.4	RTM + security update	SecurityUpdate	5035434	2024-04-09		Security update for SQL Server 2019 GDR: April 9, 2024\n" +
        "B	15.0.2104.1	RTM + security update	SecurityUpdate	5029377	2023-10-10		Security update for SQL Server 2019 GDR: October 10, 2023\n" +
        "B	15.0.2101.7	RTM + security update	SecurityUpdate	5021125	2023-02-14		Security update for SQL Server 2019 GDR: February 14, 2023\n" +
        "B	15.0.2095.3	RTM + security update	SecurityUpdate	5014356	2022-06-14		Security update for SQL Server 2019 GDR: June 14, 2022\n" +
        "B	15.0.2090.38	RTM + security update	SecurityUpdate	5010657	2022-02-08		Security update for SQL Server 2019 GDR: February 8, 2022\n" +
        "B	15.0.2080.9	RTM + security update	SecurityUpdate	4583458	2021-01-12		Security update for SQL Server 2019 GDR: January 12, 2021\n" +
        "B	15.0.2070.41	RTM	SecurityUpdate	4517790	2019-11-04		Servicing Update (GDR1) for SQL Server 2019 RTM\n" +
        "B	15.0.2000.5	RTM	Rtm		2019-11-04		Microsoft SQL Server 2019 RTM\n" +
        "B	15.0.1900.47	RC	Preview		2019-08-29		Microsoft SQL Server 2019 Release Candidate Refresh for Big Data Clusters only (RC1.1) Beta\n" +
        "B	15.0.1900.25	RC1	Preview		2019-08-21		Microsoft SQL Server 2019 Release Candidate 1 (RC1) Beta\n" +
        "B	15.0.1800.32	CTP 3.2	Preview		2019-07-24		Microsoft SQL Server 2019 Community Technology Preview 3.2 (CTP 3.2) Beta\n" +
        "B	15.0.1700.37	CTP 3.1	Preview		2019-06-26		Microsoft SQL Server 2019 Community Technology Preview 3.1 (CTP 3.1) Beta\n" +
        "B	15.0.1600.8	CTP 3.0	Preview		2019-05-22		Microsoft SQL Server 2019 Community Technology Preview 3.0 (CTP 3.0) Beta\n" +
        "B	15.0.1500.28	CTP 2.5	Preview		2019-04-23		Microsoft SQL Server 2019 Community Technology Preview 2.5 (CTP 2.5) Beta\n" +
        "B	15.0.1400.75	CTP 2.4	Preview		2019-03-26		Microsoft SQL Server 2019 Community Technology Preview 2.4 (CTP 2.4) Beta\n" +
        "B	15.0.1300.359	CTP 2.3	Preview		2019-03-01		Microsoft SQL Server 2019 Community Technology Preview 2.3 (CTP 2.3) Beta\n" +
        "B	15.0.1200.24	CTP 2.2	Preview		2018-12-11		Microsoft SQL Server 2019 Community Technology Preview 2.2 (CTP 2.2) Beta\n" +
        "B	15.0.1100.94	CTP 2.1	Preview		2018-11-06		Microsoft SQL Server 2019 Community Technology Preview 2.1 (CTP 2.1) Beta\n" +
        "B	15.0.1000.34	CTP 2.0	Preview		2018-09-24		Microsoft SQL Server 2019 Community Technology Preview 2.0 (CTP 2.0) Beta\n" +
        "R	14.0	SQL Server 2017	vNext	14.0.1000.169	2017-10-02	2022-10-11	2027-10-12\n" +
        "B	14.0.3540.1	CU31 + security update	SecurityUpdate	5102337	2026-07-14		Security update for SQL Server 2017 CU31: July 14, 2026\n" +
        "B	14.0.3530.2	CU31 + security update	SecurityUpdate	5090354	2026-05-12		Security update for SQL Server 2017 CU31: May 12, 2026\n" +
        "B	14.0.3525.1	CU31 + security update	SecurityUpdate	5084818	2026-04-14		Security update for SQL Server 2017 CU31: April 14, 2026\n" +
        "B	14.0.3520.4	CU31 + security update	SecurityUpdate	5077471	2026-03-10		Security update for SQL Server 2017 CU31: March 10, 2026\n" +
        "B	14.0.3515.1	CU31 + security update	SecurityUpdate	5068402	2025-11-11		Security update for SQL Server 2017 CU31: November 11, 2025\n" +
        "B	14.0.3505.1	CU31 + security update	SecurityUpdate	5065225	2025-09-09		Security update for SQL Server 2017 CU31: September 9, 2025\n" +
        "B	14.0.3500.1	CU31 + security update	SecurityUpdate	5063759	2025-08-12		Security update for SQL Server 2017 CU31: August 12, 2025\n" +
        "B	14.0.3495.9	CU31 + security update	SecurityUpdate	5058714	2025-07-08		Security update for SQL Server 2017 CU31: July 8, 2025\n" +
        "B	14.0.3490.10	Azure Connect FP	FeaturePack	5050533	2025-03-06		Azure Connect feature pack for SQL Server 2017\n" +
        "B	14.0.3485.1	CU31 + security update	SecurityUpdate	5046858	2024-11-12		Security update for SQL Server 2017 CU31: November 12, 2024\n" +
        "B	14.0.3480.1	CU31 + security update	SecurityUpdate	5046061	2024-10-08		Security update for SQL Server 2017 CU31: October 8, 2024\n" +
        "B	14.0.3475.1	CU31 + security update	SecurityUpdate	5042215	2024-09-10		Security update for SQL Server 2017 CU31: September 10, 2024\n" +
        "B	14.0.3471.2	CU31 + security update	SecurityUpdate	5040940	2024-07-09		Security update for SQL Server 2017 CU31: July 9, 2024\n" +
        "B	14.0.3465.1	CU31 + security update	SecurityUpdate	5029376	2023-10-10		Security update for SQL Server 2017 CU31: October 10, 2023\n" +
        "B	14.0.3460.9	CU31 + security update	SecurityUpdate	5021126	2023-02-14		Security update for SQL Server 2017 CU31: February 14, 2023\n" +
        "B	14.0.3456.2	CU31	CumulativeUpdate		2022-09-20		Cumulative update 31 (CU31) for SQL Server 2017\n" +
        "B	14.0.3451.2	CU30	CumulativeUpdate		2022-07-13		Cumulative update 30 (CU30) for SQL Server 2017\n" +
        "B	14.0.3445.2	CU29 + security update	SecurityUpdate	5014553	2022-06-14		Security update for SQL Server 2017 CU29: June 14, 2022\n" +
        "B	14.0.3436.1	CU29	CumulativeUpdate		2022-03-30		Cumulative update 29 (CU29) for SQL Server 2017\n" +
        "B	14.0.3430.2	CU28	CumulativeUpdate		2022-01-13		Cumulative update 28 (CU28) for SQL Server 2017\n" +
        "B	14.0.3421.10	CU27	CumulativeUpdate		2021-10-27		Cumulative update 27 (CU27) for SQL Server 2017\n" +
        "B	14.0.3411.3	CU26	CumulativeUpdate		2021-09-14		Cumulative update 26 (CU26) for SQL Server 2017\n" +
        "B	14.0.3401.7	CU25	CumulativeUpdate		2021-07-12		Cumulative update 25 (CU25) for SQL Server 2017\n" +
        "B	14.0.3391.2	CU24	CumulativeUpdate		2021-05-10		Cumulative update 24 (CU24) for SQL Server 2017\n" +
        "B	14.0.3381.3	CU23	CumulativeUpdate		2021-02-24		Cumulative update 23 (CU23) for SQL Server 2017\n" +
        "B	14.0.3370.1	CU22 + security update	SecurityUpdate	4583457	2021-01-12		Security update for SQL Server 2017 CU22: January 12, 2021\n" +
        "B	14.0.3356.20	CU22	CumulativeUpdate		2020-09-10		Cumulative update 22 (CU22) for SQL Server 2017\n" +
        "B	14.0.3335.7	CU21	CumulativeUpdate		2020-07-01		Cumulative update 21 (CU21) for SQL Server 2017\n" +
        "B	14.0.3294.2	CU20	CumulativeUpdate		2020-04-07		Cumulative update 20 (CU20) for SQL Server 2017\n" +
        "B	14.0.3281.6	CU19	CumulativeUpdate		2020-02-05		Cumulative update 19 (CU19) for SQL Server 2017\n" +
        "B	14.0.3257.3	CU18	CumulativeUpdate		2019-12-09		Cumulative update 18 (CU18) for SQL Server 2017\n" +
        "B	14.0.3238.1	CU17	CumulativeUpdate		2019-10-08		Cumulative update 17 (CU17) for SQL Server 2017\n" +
        "B	14.0.3223.3	CU16	CumulativeUpdate		2019-08-01		Cumulative update 16 (CU16) for SQL Server 2017\n" +
        "B	14.0.3208.1	CU15	CumulativeUpdate	4510083	2019-07-09		On-demand hotfix update package 2 for SQL Server 2017 Cumulative update 15 (CU15)\n" +
        "B	14.0.3192.2	CU15 + security update	SecurityUpdate	4505225	2019-07-09		Security update for SQL Server 2017 CU15: July 9, 2019\n" +
        "B	14.0.3164.1	CU15	CumulativeUpdate	4506633	2019-06-20		On-demand hotfix update package for SQL Server 2017 Cumulative update 15 (CU15)\n" +
        "B	14.0.3162.1	CU15	CumulativeUpdate		2019-05-24		Cumulative update 15 (CU15) for SQL Server 2017\n" +
        "B	14.0.3103.1	CU14	CumulativeUpdate	4494352	2019-05-14		Security update for SQL Server 2017 Cumulative update 14 (CU14): May 14, 2019\n" +
        "B	14.0.3076.1	CU14	CumulativeUpdate		2019-03-25		Cumulative update 14 (CU14) for SQL Server 2017\n" +
        "B	14.0.3049.1	CU13	CumulativeUpdate	4483666	2019-01-08		On-demand hotfix update package for SQL Server 2017 Cumulative update 13 (CU13)\n" +
        "B	14.0.3048.4	CU13	CumulativeUpdate		2018-12-18		Cumulative update 13 (CU13) for SQL Server 2017\n" +
        "B	14.0.3045.24	CU12	CumulativeUpdate		2018-10-24		Cumulative update 12 (CU12) for SQL Server 2017\n" +
        "B	14.0.3038.14	CU11	CumulativeUpdate		2018-09-21		Cumulative update 11 (CU11) for SQL Server 2017\n" +
        "B	14.0.3037.1	CU10	CumulativeUpdate		2018-08-27		Cumulative update 10 (CU10) for SQL Server 2017\n" +
        "B	14.0.3035.2	CU + security update	SecurityUpdate	4293805	2018-08-14		Security update for the Remote Code Execution vulnerability in SQL Server 2017 CU: August 14, 2018\n" +
        "B	14.0.3030.27	CU9	CumulativeUpdate		2018-07-18		Cumulative update 9 (CU9) for SQL Server 2017\n" +
        "B	14.0.3029.16	CU8	CumulativeUpdate		2018-06-21		Cumulative update 8 (CU8) for SQL Server 2017\n" +
        "B	14.0.3026.27	CU7	CumulativeUpdate		2018-05-23		Cumulative update 7 (CU7) for SQL Server 2017\n" +
        "B	14.0.3025.34	CU6	CumulativeUpdate		2018-04-19		Cumulative update 6 (CU6) for SQL Server 2017\n" +
        "B	14.0.3023.8	CU5	CumulativeUpdate		2018-03-20		Cumulative update 5 (CU5) for SQL Server 2017\n" +
        "B	14.0.3022.28	CU4	CumulativeUpdate		2018-02-17		Cumulative update 4 (CU4) for SQL Server 2017\n" +
        "B	14.0.3015.40	CU3	CumulativeUpdate		2018-01-04		Cumulative update 3 (CU3) for SQL Server 2017 – Security Advisory ADV180002\n" +
        "B	14.0.3008.27	CU2	CumulativeUpdate		2017-11-28		Cumulative update 2 (CU2) for SQL Server 2017\n" +
        "B	14.0.3006.16	CU1	CumulativeUpdate		2017-10-23		Cumulative update 1 (CU1) for SQL Server 2017\n" +
        "B	14.0.2120.1	RTM + security update	SecurityUpdate	5102338	2026-07-14		Security update for SQL Server 2017 GDR: July 14, 2026\n" +
        "B	14.0.2110.2	RTM + security update	SecurityUpdate	5090347	2026-05-12		Security update for SQL Server 2017 GDR: May 12, 2026\n" +
        "B	14.0.2105.1	RTM + security update	SecurityUpdate	5084819	2026-04-14		Security update for SQL Server 2017 GDR: April 14, 2026\n" +
        "B	14.0.2100.4	RTM + security update	SecurityUpdate	5077472	2026-03-10		Security update for SQL Server 2017 GDR: March 10, 2026\n" +
        "B	14.0.2095.1	RTM + security update	SecurityUpdate	5068403	2025-11-11		Security update for SQL Server 2017 GDR: November 11, 2025\n" +
        "B	14.0.2085.1	RTM + security update	SecurityUpdate	5065224	2025-09-09		Security update for SQL Server 2017 GDR: September 9, 2025\n" +
        "B	14.0.2080.1	RTM + security update	SecurityUpdate	5063760	2025-08-12		Security update for SQL Server 2017 GDR: August 12, 2025\n" +
        "B	14.0.2075.8	RTM + security update	SecurityUpdate	5058716	2025-07-08		Security update for SQL Server 2017 GDR: July 8, 2025\n" +
        "B	14.0.2070.1	RTM + security update	SecurityUpdate	5046857	2024-11-12		Security update for SQL Server 2017 GDR: November 12, 2024\n" +
        "B	14.0.2065.1	RTM + security update	SecurityUpdate	5046058	2024-10-08		Security update for SQL Server 2017 GDR: October 8, 2024\n" +
        "B	14.0.2060.1	RTM + security update	SecurityUpdate	5042217	2024-09-10		Security update for SQL Server 2017 GDR: September 10, 2024\n" +
        "B	14.0.2056.2	RTM + security update	SecurityUpdate	5040942	2024-07-09		Security update for SQL Server 2017 GDR: July 9, 2024\n" +
        "B	14.0.2052.1	RTM + security update	SecurityUpdate	5029375	2023-10-10		Security update for SQL Server 2017 GDR: October 10, 2023\n" +
        "B	14.0.2047.8	RTM + security update	SecurityUpdate	5021127	2023-02-14		Security update for SQL Server 2017 GDR: February 14, 2023\n" +
        "B	14.0.2042.3	RTM + security update	SecurityUpdate	5014354	2022-06-14		Security update for SQL Server 2017 GDR: June 14, 2022\n" +
        "B	14.0.2037.2	RTM + security update	SecurityUpdate	4583456	2021-01-12		Security update for SQL Server 2017 GDR: January 12, 2021\n" +
        "B	14.0.2027.2	RTM + security update	SecurityUpdate	4505224	2019-07-09		Security update for SQL Server 2017 GDR: July 9, 2019\n" +
        "B	14.0.2014.14	RTM + security update	SecurityUpdate	4494351	2019-05-14		Security update for SQL Server 2017 GDR: May 14, 2019\n" +
        "B	14.0.2002.14	RTM + security update	SecurityUpdate	4293803	2018-08-14		Security update for the Remote Code Execution vulnerability in SQL Server 2017 GDR: August 14, 2018\n" +
        "B	14.0.2000.63	RTM + security update	SecurityUpdate	4057122	2018-01-03		Security update for SQL Server 2017 GDR: January 3, 2018 – Security Advisory ADV180002\n" +
        "B	14.0.1000.169	RTM	Rtm		2017-10-02		Microsoft SQL Server 2017 RTM\n" +
        "B	14.0.900.75	RC2	Preview		2017-08-02		Microsoft SQL Server 2017 Release Candidate 2 (RC2) (Linux support; codename Helsinki) Beta\n" +
        "B	14.0.800.90	RC1	Preview		2017-07-17		Microsoft SQL Server 2017 Release Candidate 1 (RC1) (Linux support; codename Helsinki) Beta\n" +
        "B	14.0.600.250	CTP 2.1	Preview		2017-05-17		Microsoft SQL Server 2017 Community Technical Preview 2.1 (CTP2.1) (Linux support; codename Helsinki) Beta\n" +
        "B	14.0.500.272	CTP 2.0	Preview		2017-04-19		Microsoft SQL Server 2017 Community Technical Preview 2.0 (CTP2.0) (Linux support; codename Helsinki) Beta\n" +
        "B	14.0.405.198	CTP 1.4	Preview		2017-03-17		Microsoft SQL Server vNext Community Technology Preview 1.4 (CTP1.4) (Linux support; codename Helsinki) Beta\n" +
        "B	14.0.304.138	CTP 1.3	Preview		2017-02-17		Microsoft SQL Server vNext Community Technology Preview 1.3 (CTP1.3) (Linux support; codename Helsinki) Beta\n" +
        "B	14.0.200.24	CTP 1.2	Preview		2017-01-20		Microsoft SQL Server vNext Community Technology Preview 1.2 (CTP1.2) (Linux support; codename Helsinki) Beta\n" +
        "B	14.0.100.187	CTP 1.1	Preview		2016-12-16		Microsoft SQL Server vNext Community Technology Preview 1.1 (CTP1.1) (Linux support; codename Helsinki) Beta\n" +
        "B	14.0.1.246	CTP 1	Preview		2016-11-16		Microsoft SQL Server vNext Community Technology Preview 1 (CTP1) (Linux support; codename Helsinki) Beta\n" +
        "R	13.0	SQL Server 2016		13.0.1601.5	2016-06-01	2021-07-13	2026-07-14\n" +
        "B	13.0.7095.1	SP3 + security update	SecurityUpdate	5102339	2026-07-14		Security update for SQL Server 2016 SP3 Azure Connect Feature Pack: July 14, 2026\n" +
        "B	13.0.7085.1	SP3 + security update	SecurityUpdate	5089270	2026-05-12		Security update for SQL Server 2016 SP3 Azure Connect Feature Pack: May 12, 2026\n" +
        "B	13.0.7080.1	SP3 + security update	SecurityUpdate	5084820	2026-04-14		Security update for SQL Server 2016 SP3 Azure Connect Feature Pack: April 14, 2026\n" +
        "B	13.0.7075.5	SP3 + security update	SecurityUpdate	5077473	2026-03-10		Security update for SQL Server 2016 SP3 Azure Connect Feature Pack: March 10, 2026\n" +
        "B	13.0.7070.1	SP3 + security update	SecurityUpdate	5068400	2025-11-11		Security update for SQL Server 2016 SP3 Azure Connect Feature Pack: November 11, 2025\n" +
        "B	13.0.7065.1	SP3 + security update	SecurityUpdate	5065227	2025-09-09		Security update for SQL Server 2016 SP3 Azure Connect Feature Pack: September 9, 2025\n" +
        "B	13.0.7060.1	SP3 + security update	SecurityUpdate	5063761	2025-08-12		Security update for SQL Server 2016 SP3 Azure Connect Feature Pack: August 12, 2025\n" +
        "B	13.0.7055.9	SP3 + security update	SecurityUpdate	5058717	2025-07-08		Security update for SQL Server 2016 SP3 Azure Connect Feature Pack: July 8, 2025\n" +
        "B	13.0.7050.2	SP3 + security update	SecurityUpdate	5046856	2024-11-12		Security update for SQL Server 2016 SP3 Azure Connect Feature Pack: November 12, 2024\n" +
        "B	13.0.7045.2	SP3 + security update	SecurityUpdate	5046062	2024-10-08		Security update for SQL Server 2016 SP3 Azure Connect Feature Pack: October 8, 2024\n" +
        "B	13.0.7040.1	SP3 + security update	SecurityUpdate	5042209	2024-09-10		Security update for SQL Server 2016 SP3 Azure Connect Feature Pack: September 10, 2024\n" +
        "B	13.0.7037.1	SP3 + security update	SecurityUpdate	5040944	2024-07-09		Security update for SQL Server 2016 SP3 Azure Connect Feature Pack: July 9, 2024\n" +
        "B	13.0.7029.3	SP3 + security update	SecurityUpdate	5029187	2023-10-10		Security update for SQL Server 2016 SP3 Azure Connect Feature Pack: October 10, 2023\n" +
        "B	13.0.7024.30	SP3 + security update	SecurityUpdate	5021128	2023-02-14		Security update for SQL Server 2016 SP3 Azure Connect Feature Pack: February 14, 2023\n" +
        "B	13.0.7016.1	SP3 + security update	SecurityUpdate	5015371	2022-06-14		Security update for SQL Server 2016 SP3 Azure Connect Feature Pack: June 14, 2022\n" +
        "B	13.0.7000.253	SP3 Azure Connect FP	FeaturePack	5014242	2022-05-19		Azure Connect Feature Pack for SQL Server 2016 Service Pack 3\n" +
        "B	13.0.6500.1	SP3 + security update	SecurityUpdate	5102340	2026-07-14		Security update for SQL Server 2016 SP3 GDR: July 14, 2026\n" +
        "B	13.0.6490.1	SP3 + security update	SecurityUpdate	5089271	2026-05-12		Security update for SQL Server 2016 SP3 GDR: May 12, 2026\n" +
        "B	13.0.6485.1	SP3 + security update	SecurityUpdate	5084821	2026-04-14		Security update for SQL Server 2016 SP3 GDR: April 14, 2026\n" +
        "B	13.0.6480.4	SP3 + security update	SecurityUpdate	5077474	2026-03-10		Security update for SQL Server 2016 SP3 GDR: March 10, 2026\n" +
        "B	13.0.6475.1	SP3 + security update	SecurityUpdate	5068401	2025-11-11		Security update for SQL Server 2016 SP3 GDR: November 11, 2025\n" +
        "B	13.0.6470.1	SP3 + security update	SecurityUpdate	5065226	2025-09-09		Security update for SQL Server 2016 SP3 GDR: September 9, 2025\n" +
        "B	13.0.6465.1	SP3 + security update	SecurityUpdate	5063762	2025-08-12		Security update for SQL Server 2016 SP3 GDR: August 12, 2025\n" +
        "B	13.0.6460.7	SP3 + security update	SecurityUpdate	5058718	2025-07-08		Security update for SQL Server 2016 SP3 GDR: July 8, 2025\n" +
        "B	13.0.6455.2	SP3 + security update	SecurityUpdate	5046855	2024-11-12		Security update for SQL Server 2016 SP3 GDR: November 12, 2024\n" +
        "B	13.0.6450.1	SP3 + security update	SecurityUpdate	5046063	2024-10-08		Security update for SQL Server 2016 SP3 GDR: October 8, 2024\n" +
        "B	13.0.6445.1	SP3 + security update	SecurityUpdate	5042207	2024-09-10		Security update for SQL Server 2016 SP3 GDR: September 10, 2024\n" +
        "B	13.0.6441.1	SP3 + security update	SecurityUpdate	5040946	2024-07-09		Security update for SQL Server 2016 SP3 GDR: July 9, 2024\n" +
        "B	13.0.6435.1	SP3 + security update	SecurityUpdate	5029186	2023-10-10		Security update for SQL Server 2016 SP3 GDR: October 10, 2023\n" +
        "B	13.0.6430.49	SP3 + security update	SecurityUpdate	5021129	2023-02-14		Security update for SQL Server 2016 SP3 GDR: February 14, 2023\n" +
        "B	13.0.6419.1	SP3 + security update	SecurityUpdate	5014355	2022-06-14		Security update for SQL Server 2016 SP3 GDR: June 14, 2022\n" +
        "B	13.0.6404.1	SP3 hotfix	Hotfix	5006943	2021-10-27		On-demand hotfix update package for SQL Server 2016 Service Pack 3 (SP3)\n" +
        "B	13.0.6300.2	SP3	ServicePack	5003279	2021-09-15		Microsoft SQL Server 2016 Service Pack 3 (SP3)\n" +
        "B	13.0.5893.48	SP2 CU17 + security update	SecurityUpdate	5014351	2022-06-14		Security update for SQL Server 2016 SP2 CU17: June 14, 2022\n" +
        "B	13.0.5888.11	SP2 CU17	CumulativeUpdate	5001092	2021-03-29		Cumulative update 17 (CU17) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5882.1	SP2 CU16	CumulativeUpdate	5000645	2021-02-11		Cumulative update 16 (CU16) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5865.1	SP2 CU15 + security update	SecurityUpdate	4583461	2021-01-12		Security update for SQL Server 2016 SP2 CU15: January 12, 2021\n" +
        "B	13.0.5850.14	SP2 CU15	CumulativeUpdate	4577775	2020-09-28		Cumulative update 15 (CU15) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5830.85	SP2 CU14	CumulativeUpdate	4564903	2020-08-06		Cumulative update 14 (CU14) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5820.21	SP2 CU13	CumulativeUpdate	4549825	2020-05-28		Cumulative update 13 (CU13) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5698.0	SP2 CU12	CumulativeUpdate	4536648	2020-02-25		Cumulative update 12 (CU12) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5622.0	SP2 CU11 + security update	SecurityUpdate	4535706	2020-02-11		Security update for SQL Server 2016 SP2 CU11: February 11, 2020\n" +
        "B	13.0.5598.27	SP2 CU11	CumulativeUpdate	4527378	2019-12-09		Cumulative update 11 (CU11) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5492.2	SP2 CU10	CumulativeUpdate	4524334	2019-10-08		Cumulative update 10 (CU10) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5479.0	SP2 CU9	CumulativeUpdate	4515435	2019-09-30	1	Cumulative update 9 (CU9) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5426.0	SP2 CU8	CumulativeUpdate	4505830	2019-07-31		Cumulative update 8 (CU8) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5382.0	CU7	CumulativeUpdate	4510807	2019-07-09		On-demand hotfix update package 2 for SQL Server 2016 Service Pack 2 (SP2) Cumulative update 7 (CU7)\n" +
        "B	13.0.5366.0	SP2 CU7 + security update	SecurityUpdate	4505222	2019-07-09		Security update for SQL Server 2016 SP2 CU7 GDR: July 9, 2019\n" +
        "B	13.0.5343.1	CU7	CumulativeUpdate	4508636	2019-06-24		On-demand hotfix update package for SQL Server 2016 Service Pack 2 (SP2) Cumulative update 7 (CU7)\n" +
        "B	13.0.5337.0	SP2 CU7	CumulativeUpdate	4495256	2019-05-22		Cumulative update 7 (CU7) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5292.0	SP2 CU6	CumulativeUpdate	4488536	2019-03-19		Cumulative update 6 (CU6) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5270.0	CU5 hotfix	Hotfix	4490133	2019-02-14		On-demand hotfix update package for SQL Server 2016 SP2 CU5\n" +
        "B	13.0.5264.1	SP2 CU5	CumulativeUpdate	4475776	2019-01-23		Cumulative update 5 (CU5) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5239.0	CU4 hotfix	Hotfix	4482972	2018-12-21		On-demand hotfix update package 2 for SQL Server 2016 SP2 CU4\n" +
        "B	13.0.5233.0	SP2 CU4	CumulativeUpdate	4464106	2018-11-13		Cumulative update 4 (CU4) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5221.0	Hotfix	Hotfix	4466793	2018-10-09		FIX: Assertion error occurs when you restart the SQL Server 2016 database\n" +
        "B	13.0.5221.0	Hotfix	Hotfix	4466994	2018-10-09		FIX: \"3414\" and \"9003\" errors and a .pmm log file grows large in SQL Server 2016\n" +
        "B	13.0.5216.0	SP2 CU3	CumulativeUpdate	4458871	2018-09-21		Cumulative update 3 (CU3) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5201.2	SP2 CU + security update	SecurityUpdate	4458621	2018-08-19		Security update for the Remote Code Execution vulnerability in SQL Server 2016 SP2 CU: August 19, 2018\n" +
        "B	13.0.5161.0	SP2 CU + security update	SecurityUpdate	4293807	2018-08-14	1	Security update for the Remote Code Execution vulnerability in SQL Server 2016 SP2 CU: August 14, 2018\n" +
        "B	13.0.5153.0	SP2 CU2	CumulativeUpdate	4340355	2018-07-17		Cumulative update 2 (CU2) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5149.0	SP2 CU1	CumulativeUpdate	4135048	2018-05-30		Cumulative update 1 (CU1) for SQL Server 2016 Service Pack 2\n" +
        "B	13.0.5108.50	SP2 + security update	SecurityUpdate	5014365	2022-06-14		Security update for SQL Server 2016 SP2 GDR: June 14, 2022\n" +
        "B	13.0.5103.6	SP2 + security update	SecurityUpdate	4583460	2021-01-12		Security update for SQL Server 2016 SP2 GDR: January 12, 2021\n" +
        "B	13.0.5102.14	SP2 + security update	SecurityUpdate	4532097	2020-02-11		Security update for SQL Server 2016 SP2 GDR: February 11, 2020\n" +
        "B	13.0.5101.9	SP2 + security update	SecurityUpdate	4505220	2019-07-09		Security update for SQL Server 2016 SP2 GDR: July 9, 2019\n" +
        "B	13.0.5081.1	SP2 + security update	SecurityUpdate	4293802	2018-08-14		Security update for the Remote Code Execution vulnerability in SQL Server 2016 SP2 GDR: August 14, 2018\n" +
        "B	13.0.5026.0	SP2	ServicePack	4052908	2018-04-24		Microsoft SQL Server 2016 Service Pack 2 (SP2)\n" +
        "B	13.0.4604.0	SP1 CU15 + security update	SecurityUpdate	4505221	2019-07-09		Security update for SQL Server 2016 SP1 CU15 GDR: July 9, 2019\n" +
        "B	13.0.4577.0	CU15	CumulativeUpdate	4508471	2019-06-20		On-demand hotfix update package for SQL Server 2016 Service Pack 1 (SP1) Cumulative update 15 (CU15)\n" +
        "B	13.0.4574.0	SP1 CU15	CumulativeUpdate	4495257	2019-05-16		Cumulative update 15 (CU15) for SQL Server 2016 Service Pack 1\n" +
        "B	13.0.4560.0	SP1 CU14	CumulativeUpdate	4488535	2019-03-19		Cumulative update 14 (CU14) for SQL Server 2016 Service Pack 1\n" +
        "B	13.0.4550.1	SP1 CU13	CumulativeUpdate	4475775	2019-01-23		Cumulative update 13 (CU13) for SQL Server 2016 Service Pack 1\n" +
        "B	13.0.4541.0	SP1 CU12	CumulativeUpdate	4464343	2018-11-13		Cumulative update 12 (CU12) for SQL Server 2016 Service Pack 1\n" +
        "B	13.0.4531.0	Hotfix	Hotfix	4465443	2018-09-27		FIX: The \"modification_counter\" in DMV sys.dm_db_stats_properties shows incorrect value when partitions are merged through ALTER PARTITION in SQL Server 2016\n" +
        "B	13.0.4528.0	SP1 CU11	CumulativeUpdate	4459676	2018-09-18		Cumulative update 11 (CU11) for SQL Server 2016 Service Pack 1\n" +
        "B	13.0.4522.0	SP1 CU + security update	SecurityUpdate	4293808	2018-08-14		Security update for the Remote Code Execution vulnerability in SQL Server 2016 SP1 CU: August 14, 2018\n" +
        "B	13.0.4514.0	SP1 CU10	CumulativeUpdate	4341569	2018-07-16		Cumulative update 10 (CU10) for SQL Server 2016 Service Pack 1\n" +
        "B	13.0.4502.0	SP1 CU9	CumulativeUpdate	4100997	2018-05-30		Cumulative update 9 (CU9) for SQL Server 2016 Service Pack 1\n" +
        "B	13.0.4477.0	Hotfix	Hotfix	4099490	2018-06-02		On-demand hotfix update package for SQL Server 2016 SP1\n" +
        "B	13.0.4474.0	SP1 CU8	CumulativeUpdate	4077064	2018-03-19		Cumulative update 8 (CU8) for SQL Server 2016 Service Pack 1\n" +
        "B	13.0.4466.4	SP1 CU7	CumulativeUpdate	4057119	2018-01-04		Cumulative update 7 (CU7) for SQL Server 2016 Service Pack 1 – Security Advisory ADV180002\n" +
        "B	13.0.4457.0	SP1 CU6	CumulativeUpdate	4037354	2017-11-21		Cumulative update 6 (CU6) for SQL Server 2016 Service Pack 1\n" +
        "B	13.0.4451.0	SP1 CU5	CumulativeUpdate	4040714	2017-09-18		Cumulative update 5 (CU5) for SQL Server 2016 Service Pack 1\n" +
        "B	13.0.4446.0	SP1 CU4	CumulativeUpdate	4024305	2017-08-08		Cumulative update 4 (CU4) for SQL Server 2016 Service Pack 1\n" +
        "B	13.0.4435.0	SP1 CU3	CumulativeUpdate	4019916	2017-05-15		Cumulative update 3 (CU3) for SQL Server 2016 Service Pack 1\n" +
        "B	13.0.4422.0	SP1 CU2	CumulativeUpdate	4013106	2017-03-22		Cumulative update 2 (CU2) for SQL Server 2016 Service Pack 1\n" +
        "B	13.0.4411.0	SP1 CU1	CumulativeUpdate	3208177	2017-01-18		Cumulative update 1 (CU1) for SQL Server 2016 Service Pack 1\n" +
        "B	13.0.4259.0	SP1 + security update	SecurityUpdate	4505219	2019-07-09		Security update for SQL Server 2016 SP1 GDR: July 9, 2019\n" +
        "B	13.0.4224.16	SP1 + security update	SecurityUpdate	4458842	2018-08-22		Security update for the Remote Code Execution vulnerability in SQL Server 2016 SP1 GDR: August 22, 2018\n" +
        "B	13.0.4223.10	SP1 + security update	SecurityUpdate	4293801	2018-08-14	1	Security update for the Remote Code Execution vulnerability in SQL Server 2016 SP1 GDR: August 14, 2018\n" +
        "B	13.0.4210.6	SP1 + security update	SecurityUpdate	4057118	2018-01-03		Description of the security update for SQL Server 2016 SP1 GDR: January 3, 2018 – Security Advisory ADV180002\n" +
        "B	13.0.4206.0	RTM + security update	SecurityUpdate	4019089	2017-08-08		Security update for SQL Server 2016 Service Pack 1 GDR: August 8, 2017\n" +
        "B	13.0.4202.2	GDR	SecurityUpdate	3210089	2016-12-16		GDR update package for SQL Server 2016 SP1\n" +
        "B	13.0.4199.0		Unknown	3207512	2016-11-23		Important update for SQL Server 2016 SP1 Reporting Services\n" +
        "B	13.0.4001.0	SP1	ServicePack	3182545	2016-11-16		Microsoft SQL Server 2016 Service Pack 1 (SP1)\n" +
        "B	13.0.2218.0	CU + security update	SecurityUpdate	4058559	2018-01-06		Description of the security update for SQL Server 2016 CU: January 6, 2018 – Security Advisory ADV180002\n" +
        "B	13.0.2216.0	CU9	CumulativeUpdate	4037357	2017-11-21		Cumulative update 9 (CU9) for SQL Server 2016\n" +
        "B	13.0.2213.0	CU8	CumulativeUpdate	4040713	2017-09-18		Cumulative update 8 (CU8) for SQL Server 2016\n" +
        "B	13.0.2210.0	CU7	CumulativeUpdate	4024304	2017-08-08		Cumulative update 7 (CU7) for SQL Server 2016\n" +
        "B	13.0.2204.0	CU6	CumulativeUpdate	4019914	2017-05-15		Cumulative update 6 (CU6) for SQL Server 2016\n" +
        "B	13.0.2197.0	CU5	CumulativeUpdate	4013105	2017-03-21		Cumulative update 5 (CU5) for SQL Server 2016\n" +
        "B	13.0.2193.0	CU4	CumulativeUpdate	3205052	2017-01-18		Cumulative update 4 (CU4) for SQL Server 2016\n" +
        "B	13.0.2190.2	CU3 hotfix	Hotfix	3210110	2016-12-16		On-demand hotfix update package for SQL Server 2016 CU3\n" +
        "B	13.0.2186.6	CU3	CumulativeUpdate	3205413	2016-11-08		Cumulative update 3 (CU3) for SQL Server 2016\n" +
        "B	13.0.2186.6	CU + security update	SecurityUpdate	3194717	2016-11-08		MS16-136: Description of the security update for SQL Server 2016 CU: November 8, 2016\n" +
        "B	13.0.2170.0	CU2 hotfix	Hotfix	3199171	2016-11-01		On-demand hotfix update package for SQL Server 2016 CU2\n" +
        "B	13.0.2169.0	CU2 hotfix	Hotfix	3195813	2016-10-26		On-demand hotfix update package for SQL Server 2016 CU2\n" +
        "B	13.0.2164.0	CU2	CumulativeUpdate	3182270	2016-09-22		Cumulative update 2 (CU2) for SQL Server 2016\n" +
        "B	13.0.2149.0	CU1	CumulativeUpdate	3164674	2016-07-26		Cumulative update 1 (CU1) for SQL Server 2016\n" +
        "B	13.0.1745.2	RTM + security update	SecurityUpdate	4058560	2018-01-06		Description of the security update for SQL Server 2016 GDR: January 6, 2018 – Security Advisory ADV180002\n" +
        "B	13.0.1742.0	RTM + security update	SecurityUpdate	4019088	2017-08-08		Security update for SQL Server 2016 RTM GDR: August 8, 2017\n" +
        "B	13.0.1728.2	RTM	SecurityUpdate	3210111	2016-12-16		GDR update package for SQL Server 2016 RTM\n" +
        "B	13.0.1722.0	RTM + security update	SecurityUpdate	3194716	2016-11-08		MS16-136: Description of the security update for SQL Server 2016 GDR: November 8, 2016\n" +
        "B	13.0.1711.0		Unknown	3179258	2016-08-17		Processing a partition causes data loss on other partitions after the database is restored in SQL Server 2016 (1200)\n" +
        "B	13.0.1708.0		Unknown	3164398	2016-06-03		Critical update for SQL Server 2016 MSVCRT prerequisites\n" +
        "B	13.0.1601.5	RTM	Rtm		2016-06-01		Microsoft SQL Server 2016 RTM\n" +
        "B	13.0.1400.361	RC3	Preview		2016-04-15		Microsoft SQL Server 2016 Release Candidate 3 (RC3) Beta\n" +
        "B	13.0.1300.275	RC2	Preview		2016-04-01		Microsoft SQL Server 2016 Release Candidate 2 (RC2) Beta\n" +
        "B	13.0.1200.242	RC1	Preview		2016-03-18		Microsoft SQL Server 2016 Release Candidate 1 (RC1) Beta\n" +
        "B	13.0.1100.288	RC0	Preview		2016-03-07		Microsoft SQL Server 2016 Release Candidate 0 (RC0) Beta\n" +
        "B	13.0.1000.281	CTP 3.3	Preview		2016-02-03		Microsoft SQL Server 2016 Community Technology Preview 3.3 (CTP3.3) Beta\n" +
        "B	13.0.900.73	CTP 3.2	Preview		2015-12-16		Microsoft SQL Server 2016 Community Technology Preview 3.2 (CTP3.2) Beta\n" +
        "B	13.0.800.11	CTP 3.1	Preview		2015-11-30		Microsoft SQL Server 2016 Community Technology Preview 3.1 (CTP3.1) Beta\n" +
        "B	13.0.700.139	CTP 3.0	Preview		2015-10-28		Microsoft SQL Server 2016 Community Technology Preview 3.0 (CTP3.0) Beta\n" +
        "B	13.0.600.65	CTP 2.4	Preview		2015-09-30		Microsoft SQL Server 2016 Community Technology Preview 2.4 (CTP2.4) Beta\n" +
        "B	13.0.500.53	CTP 2.3	Preview		2015-08-28		Microsoft SQL Server 2016 Community Technology Preview 2.3 (CTP2.3) Beta\n" +
        "B	13.0.407.1	CTP 2.2	Preview		2015-07-23		Microsoft SQL Server 2016 Community Technology Preview 2.2 (CTP2.2) Beta\n" +
        "B	13.0.400.91	CTP 2.2	Preview		2015-07-22	1	Microsoft SQL Server 2016 Community Technology Preview 2.2 (CTP2.2) Beta\n" +
        "B	13.0.300.44	CTP 2.1	Preview		2015-06-24		Microsoft SQL Server 2016 Community Technology Preview 2.1 (CTP2.1) Beta\n" +
        "B	13.0.200.172	CTP 2	Preview		2015-05-27		Microsoft SQL Server 2016 Community Technology Preview 2 (CTP2) Beta\n" +
        "R	12.0	SQL Server 2014		12.0.2000.8	2014-04-01	2019-07-09	2024-07-09\n" +
        "B	12.0.6449.1	SP3 CU4 + security update	SecurityUpdate	5029185	2023-10-10		Security update for SQL Server 2014 SP3 CU4: October 10, 2023\n" +
        "B	12.0.6444.4	SP3 CU4 + security update	SecurityUpdate	5021045	2023-02-14		Security update for SQL Server 2014 SP3 CU4: February 14, 2023\n" +
        "B	12.0.6439.10	SP3 CU4 + security update	SecurityUpdate	5014164	2022-06-14		Security update for SQL Server 2014 SP3 CU4: June 14, 2022\n" +
        "B	12.0.6433.1	SP3 CU4 + security update	SecurityUpdate	4583462	2021-01-12		Security update for SQL Server 2014 SP3 CU4: January 12, 2021\n" +
        "B	12.0.6372.1	SP3 CU4 + security update	SecurityUpdate	4535288	2020-02-11		Security update for SQL Server 2014 SP3 CU4: February 11, 2020\n" +
        "B	12.0.6329.1	SP3 CU4	CumulativeUpdate	4500181	2019-07-29		Cumulative update package 4 (CU4) for SQL Server 2014 Service Pack 3\n" +
        "B	12.0.6293.0	SP3 CU3 + security update	SecurityUpdate	4505422	2019-07-09		Security update for SQL Server 2014 SP3 CU3 GDR: July 9, 2019\n" +
        "B	12.0.6259.0	SP3 CU3	CumulativeUpdate	4491539	2019-04-16		Cumulative update package 3 (CU3) for SQL Server 2014 Service Pack 3\n" +
        "B	12.0.6214.1	SP3 CU2	CumulativeUpdate	4482960	2019-02-19		Cumulative update package 2 (CU2) for SQL Server 2014 Service Pack 3\n" +
        "B	12.0.6205.1	SP3 CU1	CumulativeUpdate	4470220	2018-12-12		Cumulative update package 1 (CU1) for SQL Server 2014 Service Pack 3\n" +
        "B	12.0.6179.1	SP3 + security update	SecurityUpdate	5029184	2023-10-10		Security update for SQL Server 2014 SP3 GDR: October 10, 2023\n" +
        "B	12.0.6174.8	SP3 + security update	SecurityUpdate	5021037	2023-02-14		Security update for SQL Server 2014 SP3 GDR: February 14, 2023\n" +
        "B	12.0.6169.19	SP3 + security update	SecurityUpdate	5014165	2022-06-14		Security update for SQL Server 2014 SP3 GDR: June 14, 2022\n" +
        "B	12.0.6164.21	SP3 + security update	SecurityUpdate	4583463	2021-01-12		Security update for SQL Server 2014 SP3 GDR: January 12, 2021\n" +
        "B	12.0.6118.4	SP3 + security update	SecurityUpdate	4532095	2020-02-11		Security update for SQL Server 2014 SP3 GDR: February 11, 2020\n" +
        "B	12.0.6108.1	SP3 + security update	SecurityUpdate	4505218	2019-07-09		Security update for SQL Server 2014 SP3 GDR: July 9, 2019\n" +
        "B	12.0.6024.0	SP3	ServicePack	4022619	2018-10-30		SQL Server 2014 Service Pack 3 (SP3)\n" +
        "B	12.0.5687.1	SP2 CU18	CumulativeUpdate	4500180	2019-07-29		Cumulative update package 18 (CU18) for SQL Server 2014 Service Pack 2\n" +
        "B	12.0.5659.1	SP2 CU17 + security update	SecurityUpdate	4505419	2019-07-09		Security update for SQL Server 2014 SP2 CU17 GDR: July 9, 2019\n" +
        "B	12.0.5632.1	SP2 CU17	CumulativeUpdate	4491540	2019-04-16		Cumulative update package 17 (CU17) for SQL Server 2014 Service Pack 2\n" +
        "B	12.0.5626.1	SP2 CU16	CumulativeUpdate	4482967	2019-02-19		Cumulative update package 16 (CU16) for SQL Server 2014 Service Pack 2\n" +
        "B	12.0.5605.1	SP2 CU15	CumulativeUpdate	4469137	2018-12-12		Cumulative update package 15 (CU15) for SQL Server 2014 Service Pack 2\n" +
        "B	12.0.5600.1	SP2 CU14	CumulativeUpdate	4459860	2018-10-15		Cumulative update package 14 (CU14) for SQL Server 2014 Service Pack 2\n" +
        "B	12.0.5590.1	SP2 CU13	CumulativeUpdate	4456287	2018-08-27		Cumulative update package 13 (CU13) for SQL Server 2014 Service Pack 2\n" +
        "B	12.0.5589.7	SP2 CU12	CumulativeUpdate	4130489	2018-06-18		Cumulative update package 12 (CU12) for SQL Server 2014 Service Pack 2\n" +
        "B	12.0.5579.0	SP2 CU11	CumulativeUpdate	4077063	2018-03-19		Cumulative update package 11 (CU11) for SQL Server 2014 Service Pack 2\n" +
        "B	12.0.5571.0	SP2 CU10	CumulativeUpdate	4052725	2018-01-16		Cumulative update package 10 (CU10) for SQL Server 2014 Service Pack 2 – Security Advisory ADV180002\n" +
        "B	12.0.5563.0	SP2 CU9	CumulativeUpdate	4055557	2017-12-19		Cumulative update package 9 (CU9) for SQL Server 2014 Service Pack 2\n" +
        "B	12.0.5557.0	SP2 CU8	CumulativeUpdate	4037356	2017-10-17		Cumulative update package 8 (CU8) for SQL Server 2014 Service Pack 2\n" +
        "B	12.0.5556.0	SP2 CU7	CumulativeUpdate	4032541	2017-08-29		Cumulative update package 7 (CU7) for SQL Server 2014 Service Pack 2\n" +
        "B	12.0.5553.0	SP2 CU6	CumulativeUpdate	4019094	2017-08-08		Cumulative update package 6 (CU6) for SQL Server 2014 Service Pack 2\n" +
        "B	12.0.5546.0	SP2 CU5	CumulativeUpdate	4013098	2017-04-18		Cumulative update package 5 (CU5) for SQL Server 2014 Service Pack 2\n" +
        "B	12.0.5540.0	SP2 CU4	CumulativeUpdate	4010394	2017-02-21		Cumulative update package 4 (CU4) for SQL Server 2014 Service Pack 2\n" +
        "B	12.0.5538.0	SP2 CU3	CumulativeUpdate	3204388	2016-12-28		Cumulative update package 3 (CU3) for SQL Server 2014 Service Pack 2 - The article incorrectly says it's version 12.0.5537\n" +
        "B	12.0.5532.0	CU + security update	SecurityUpdate	3194718	2016-11-08		MS16-136: Description of the security update for SQL Server 2014 Service Pack 2 CU: November 8, 2016\n" +
        "B	12.0.5522.0	SP2 CU2	CumulativeUpdate	3188778	2016-10-18		Cumulative update package 2 (CU2) for SQL Server 2014 Service Pack 2\n" +
        "B	12.0.5511.0	SP2 CU1	CumulativeUpdate	3178925	2016-08-26		Cumulative update package 1 (CU1) for SQL Server 2014 Service Pack 2\n" +
        "B	12.0.5223.6	SP2 + security update	SecurityUpdate	4505217	2019-07-09		Security update for SQL Server 2014 SP2 GDR: July 9, 2019\n" +
        "B	12.0.5214.6	RTM + security update	SecurityUpdate	4057120	2018-01-16		Security update for SQL Server 2014 Service Pack 2 GDR: January 16, 2018 – Security Advisory ADV180002\n" +
        "B	12.0.5207.0	RTM + security update	SecurityUpdate	4019093	2017-08-08		Security update for SQL Server 2014 Service Pack 2 GDR: August 8, 2017\n" +
        "B	12.0.5203.0	RTM + security update	SecurityUpdate	3194714	2016-11-08		MS16-136: Description of the security update for SQL Server 2014 Service Pack 2 GDR: November 8, 2016\n" +
        "B	12.0.5000.0	SP2	ServicePack		2016-07-11		SQL Server 2014 Service Pack 2 (SP2)\n" +
        "B	12.0.4522.0	SP1 CU13	CumulativeUpdate	4019099	2017-08-08		Cumulative update package 13 (CU13) for SQL Server 2014 Service Pack 1\n" +
        "B	12.0.4511.0	SP1 CU12	CumulativeUpdate	4017793	2017-04-18		Cumulative update package 12 (CU12) for SQL Server 2014 Service Pack 1\n" +
        "B	12.0.4502.0	SP1 CU11	CumulativeUpdate	4010392	2017-02-21		Cumulative update package 11 (CU11) for SQL Server 2014 Service Pack 1\n" +
        "B	12.0.4491.0	SP1 CU10	CumulativeUpdate	3204399	2016-12-28		Cumulative update package 10 (CU10) for SQL Server 2014 Service Pack 1\n" +
        "B	12.0.4487.0	CU + security update	SecurityUpdate	3194722	2016-11-08		MS16-136: Description of the security update for SQL Server 2014 Service Pack 1 CU: November 8, 2016\n" +
        "B	12.0.4474.0	SP1 CU9	CumulativeUpdate	3186964	2016-10-18		Cumulative update package 9 (CU9) for SQL Server 2014 Service Pack 1\n" +
        "B	12.0.4468.0	SP1 CU8	CumulativeUpdate	3174038	2016-08-15		Cumulative update package 8 (CU8) for SQL Server 2014 Service Pack 1\n" +
        "B	12.0.4463.0		Unknown	3174370	2016-08-04		A memory leak occurs when you use Azure Storage in SQL Server 2014\n" +
        "B	12.0.4459.0	SP1 CU7	CumulativeUpdate	3162659	2016-06-20		Cumulative update package 7 (CU7) for SQL Server 2014 Service Pack 1\n" +
        "B	12.0.4457.1	SP1 CU6	CumulativeUpdate	3167392	2016-05-31		REFRESHED Cumulative update package 6 (CU6) for SQL Server 2014 Service Pack 1\n" +
        "B	12.0.4449.1	SP1 CU6	CumulativeUpdate	3144524	2016-04-19		DEPRECATED Cumulative update package 6 (CU6) for SQL Server 2014 Service Pack 1\n" +
        "B	12.0.4439.1	SP1 CU5	CumulativeUpdate	3130926	2016-02-22		Cumulative update package 5 (CU5) for SQL Server 2014 Service Pack 1\n" +
        "B	12.0.4437.0	SP1 hotfix	Hotfix	3130999	2016-02-05		On-demand hotfix update package for SQL Server 2014 Service Pack 1 Cumulative Update 4\n" +
        "B	12.0.4436.0	SP1 CU4	CumulativeUpdate	3106660	2015-12-22		Cumulative update package 4 (CU4) for SQL Server 2014 Service Pack 1\n" +
        "B	12.0.4433.0	Hotfix	Hotfix	3119148	2015-12-09		FIX: Error 3203 and a SQL Server 2014 backup job can't restart when a network failure occurs\n" +
        "B	12.0.4432.0	Hotfix	Hotfix	3097972	2015-11-19		FIX: Error when your stored procedure calls another stored procedure on linked server in SQL Server 2014\n" +
        "B	12.0.4427.24	SP1 CU3	CumulativeUpdate	3094221	2015-10-21		Cumulative update package 3 (CU3) for SQL Server 2014 Service Pack 1\n" +
        "B	12.0.4422.0	SP1 CU2	CumulativeUpdate	3075950	2015-08-17		Cumulative update package 2 (CU2) for SQL Server 2014 Service Pack 1\n" +
        "B	12.0.4419.0	Hotfix	Hotfix	3078973	2015-07-24		An on-demand hotfix update package is available for SQL Server 2014 SP1\n" +
        "B	12.0.4416.0	SP1 CU1	CumulativeUpdate	3067839	2015-06-22		Cumulative update package 1 (CU1) for SQL Server 2014 Service Pack 1\n" +
        "B	12.0.4237.0	RTM + security update	SecurityUpdate	4019091	2017-08-08		Security update for SQL Server 2014 Service Pack 1 GDR: August 8, 2017\n" +
        "B	12.0.4232.0	RTM + security update	SecurityUpdate	3194720	2016-11-08		MS16-136: Description of the security update for SQL Server 2014 Service Pack 1 GDR: November 8, 2016\n" +
        "B	12.0.4219.0		Unknown	3098852	2016-01-27		TLS 1.2 support for SQL Server 2014 SP1\n" +
        "B	12.0.4213.0	RTM + security update	SecurityUpdate	3070446	2015-07-14		MS15-058: Description of the nonsecurity update for SQL Server 2014 Service Pack 1 GDR: July 14, 2015\n" +
        "B	12.0.4100.1	SP1	ServicePack		2015-05-14		SQL Server 2014 Service Pack 1 (SP1)\n" +
        "B	12.0.4050.0	SP1	ServicePack		2015-04-15	1	SQL Server 2014 Service Pack 1 (SP1)\n" +
        "B	12.0.2569.0	CU14	CumulativeUpdate	3158271	2016-06-20		Cumulative update package 14 (CU14) for SQL Server 2014\n" +
        "B	12.0.2568.0	CU13	CumulativeUpdate	3144517	2016-04-18		Cumulative update package 13 (CU13) for SQL Server 2014\n" +
        "B	12.0.2564.0	CU12	CumulativeUpdate	3130923	2016-02-22		Cumulative update package 12 (CU12) for SQL Server 2014\n" +
        "B	12.0.2560.0	CU11	CumulativeUpdate	3106659	2015-12-22		Cumulative update package 11 (CU11) for SQL Server 2014\n" +
        "B	12.0.2556.4	CU10	CumulativeUpdate	3094220	2015-10-20		Cumulative update package 10 (CU10) for SQL Server 2014\n" +
        "B	12.0.2553.0	CU9	CumulativeUpdate	3075949	2015-08-17		Cumulative update package 9 (CU9) for SQL Server 2014\n" +
        "B	12.0.2548.0		SecurityUpdate	3045323	2015-07-14		MS15-058: Description of the security update for SQL Server 2014 QFE: July 14, 2015\n" +
        "B	12.0.2546.0	CU8	CumulativeUpdate	3067836	2015-06-22		Cumulative update package 8 (CU8) for SQL Server 2014\n" +
        "B	12.0.2506.0		Unknown	3063054	2015-05-19		Update enables Premium Storage support for Data files on Azure Storage and resolves backup failures\n" +
        "B	12.0.2505.0	Hotfix	Hotfix	3052167	2015-05-19		FIX: Error 1205 when you execute parallel query that contains outer join operators in SQL Server 2014\n" +
        "B	12.0.2504.0	Hotfix	Hotfix	2999809	2015-05-05		FIX: Poor performance when a query contains table joins in SQL Server 2014\n" +
        "B	12.0.2504.0	Hotfix	Hotfix	3058512	2015-05-05		FIX: Unpivot Transformation task changes null to zero or empty strings in SSIS 2014\n" +
        "B	12.0.2495.0	CU7	CumulativeUpdate	3046038	2015-04-23		Cumulative update package 7 (CU7) for SQL Server 2014\n" +
        "B	12.0.2488.0	Hotfix	Hotfix	3048751	2015-04-01		FIX: Deadlock cannot be resolved automatically when you run a SELECT query that can result in a parallel batch-mode scan\n" +
        "B	12.0.2485.0	Hotfix	Hotfix	3043788	2015-03-16		An on-demand hotfix update package is available for SQL Server 2014\n" +
        "B	12.0.2480.0	CU6	CumulativeUpdate	3031047	2015-02-16		Cumulative update package 6 (CU6) for SQL Server 2014\n" +
        "B	12.0.2474.0	Hotfix	Hotfix	3034679	2015-05-15		FIX: AlwaysOn availability groups are reported as NOT SYNCHRONIZING\n" +
        "B	12.0.2472.0	Hotfix	Hotfix	3032087	2015-01-28		FIX: Cannot show requested dialog after you connect to the latest SQL Database Update V12 (preview) with SQL Server 2014\n" +
        "B	12.0.2464.0		Unknown	3024815	2015-01-05		Large query compilation waits on RESOURCE_SEMAPHORE_QUERY_COMPILE in SQL Server 2014\n" +
        "B	12.0.2456.0	CU5	CumulativeUpdate	3011055	2014-12-18		Cumulative update package 5 (CU5) for SQL Server 2014\n" +
        "B	12.0.2436.0	Hotfix	Hotfix	3014867	2014-11-27		FIX: \"Remote hardening failure\" exception cannot be caught and a potential data loss when you use SQL Server 2014\n" +
        "B	12.0.2430.0	CU4	CumulativeUpdate	2999197	2014-10-21		Cumulative update package 4 (CU4) for SQL Server 2014\n" +
        "B	12.0.2423.0	Hotfix	Hotfix	3007050	2014-10-22		FIX: RTDATA_LIST waits when you run natively stored procedures that encounter expected failures in SQL Server 2014\n" +
        "B	12.0.2405.0	Hotfix	Hotfix	2999809	2014-09-25		FIX: Poor performance when a query contains table joins in SQL Server 2014\n" +
        "B	12.0.2402.0	CU3	CumulativeUpdate	2984923	2014-08-18		Cumulative update package 3 (CU3) for SQL Server 2014\n" +
        "B	12.0.2381.0		SecurityUpdate	2977316	2014-08-12		MS14-044: Description of the security update for SQL Server 2014 (QFE)\n" +
        "B	12.0.2370.0	CU2	CumulativeUpdate	2967546	2014-06-27		Cumulative update package 2 (CU2) for SQL Server 2014\n" +
        "B	12.0.2342.0	CU1	CumulativeUpdate	2931693	2014-04-21		Cumulative update package 1 (CU1) for SQL Server 2014\n" +
        "B	12.0.2271.0	RTM	Rtm	3098856	2016-01-27		TLS 1.2 support for SQL Server 2014 RTM\n" +
        "B	12.0.2269.0	RTM + security update	SecurityUpdate	3045324	2015-07-14		MS15-058: Description of the security update for SQL Server 2014 GDR: July 14, 2015\n" +
        "B	12.0.2254.0	RTM + security update	SecurityUpdate	2977315	2014-08-12		MS14-044: Description of the security update for SQL Server 2014 (GDR)\n" +
        "B	12.0.2000.8	RTM	Rtm		2014-04-01		SQL Server 2014 RTM\n" +
        "B	12.0.1524.0	CTP 2	Preview		2013-10-15		Microsoft SQL Server 2014 Community Technology Preview 2 (CTP2) Beta\n" +
        "R	11.0	SQL Server 2012	Denali	11.0.2100.60	2012-03-06	2017-07-11	2022-07-12\n" +
        "B	11.0.9120.0	CTP 1	Preview		2013-06-25		Microsoft SQL Server 2014 Community Technology Preview 1 (CTP1) Beta\n" +
        "B	11.0.9000.5	CTP 3	Preview		2012-11-27		Microsoft SQL Server 2012 With Power View For Multidimensional Models Customer Technology Preview (CTP3) Beta\n" +
        "B	11.0.7512.11	SP4 + security update	SecurityUpdate	5021123	2023-02-14		Security update for SQL Server 2012 SP4 GDR: February 14, 2023\n" +
        "B	11.0.7507.2	SP4 + security update	SecurityUpdate	4583465	2021-01-12		Security update for SQL Server 2012 SP4 GDR: January 12, 2021\n" +
        "B	11.0.7493.4	SP4 + security update	SecurityUpdate	4532098	2020-02-11		Security update for SQL Server 2012 SP4 GDR: February 11, 2020\n" +
        "B	11.0.7469.6	Hotfix	Hotfix	4091266	2018-03-28		On-demand hotfix update package for SQL Server 2012 SP4\n" +
        "B	11.0.7462.6	SP4 + security update	SecurityUpdate	4057116	2018-01-12		Description of the security update for SQL Server 2012 SP4 GDR: January 12, 2018 – Security Advisory ADV180002\n" +
        "B	11.0.7001.0	SP4	ServicePack		2017-10-05		SQL Server 2012 Service Pack 4 (SP4)\n" +
        "B	11.0.6615.2	SP3 CU + security update	SecurityUpdate	4057121	2018-01-16		Description of the security update for SQL Server 2012 SP3 CU: January 16, 2018 – Security Advisory ADV180002\n" +
        "B	11.0.6607.3	SP3 CU10	CumulativeUpdate	4025925	2017-08-08		Cumulative update package 10 (CU10) for SQL Server 2012 Service Pack 3\n" +
        "B	11.0.6607.3	CU + security update	SecurityUpdate	4019090	2017-08-08		Security update for SQL Server 2012 Service Pack 3 CU: August 8, 2017\n" +
        "B	11.0.6598.0	SP3 CU9	CumulativeUpdate	4016762	2017-05-15		Cumulative update package 9 (CU9) for SQL Server 2012 Service Pack 3\n" +
        "B	11.0.6594.0	SP3 CU8	CumulativeUpdate	4013104	2017-03-21		Cumulative update package 8 (CU8) for SQL Server 2012 Service Pack 3\n" +
        "B	11.0.6579.0	SP3 CU7	CumulativeUpdate	3205051	2017-01-17		Cumulative update package 7 (CU7) for SQL Server 2012 Service Pack 3\n" +
        "B	11.0.6567.0	SP3 CU6	CumulativeUpdate	3194992	2016-11-17		Cumulative update package 6 (CU6) for SQL Server 2012 Service Pack 3\n" +
        "B	11.0.6567.0	CU + security update	SecurityUpdate	3194724	2016-11-08		MS16-136: Description of the security update for SQL Server 2012 Service Pack 3 CU: November 8, 2016\n" +
        "B	11.0.6544.0	SP3 CU5	CumulativeUpdate	3180915	2016-09-21		Cumulative update package 5 (CU5) for SQL Server 2012 Service Pack 3\n" +
        "B	11.0.6540.0	SP3 CU4	CumulativeUpdate	3165264	2016-07-19		Cumulative update package 4 (CU4) for SQL Server 2012 Service Pack 3\n" +
        "B	11.0.6537.0	SP3 CU3	CumulativeUpdate	3152635	2016-05-17		Cumulative update package 3 (CU3) for SQL Server 2012 Service Pack 3\n" +
        "B	11.0.6523.0	SP3 CU2	CumulativeUpdate	3137746	2016-03-22		Cumulative update package 2 (CU2) for SQL Server 2012 Service Pack 3\n" +
        "B	11.0.6518.0	SP3 CU1	CumulativeUpdate	3123299	2016-01-19		Cumulative update package 1 (CU1) for SQL Server 2012 Service Pack 3\n" +
        "B	11.0.6260.1	SP3 + security update	SecurityUpdate	4057115	2018-01-16		Description of the security update for SQL Server 2012 SP3 GDR: January 16, 2018 – Security Advisory ADV180002\n" +
        "B	11.0.6251.0	RTM + security update	SecurityUpdate	4019092	2017-08-08		Description of the security update for SQL Server 2012 Service Pack 3 GDR: August 8, 2017\n" +
        "B	11.0.6248.0	RTM + security update	SecurityUpdate	3194721	2016-11-08		MS16-136: Description of the security update for SQL Server 2012 Service Pack 3 GDR: November 8, 2016\n" +
        "B	11.0.6216.27	GDR	SecurityUpdate	3135244	2016-01-27		TLS 1.2 support for SQL Server 2012 SP3 GDR\n" +
        "B	11.0.6020.0	SP3	ServicePack		2015-11-23		SQL Server 2012 Service Pack 3 (SP3)\n" +
        "B	11.0.5678.0	SP2 CU16	CumulativeUpdate	3205054	2017-01-18		Cumulative update package 16 (CU16) for SQL Server 2012 Service Pack 2\n" +
        "B	11.0.5676.0	SP2 CU15	CumulativeUpdate	3205416	2016-11-17		Cumulative update package 15 (CU15) for SQL Server 2012 Service Pack 2\n" +
        "B	11.0.5676.0	CU + security update	SecurityUpdate	3194725	2016-11-08		MS16-136: Description of the security update for SQL Server 2012 Service Pack 2 CU: November 8, 2016\n" +
        "B	11.0.5657.0	SP2 CU14	CumulativeUpdate	3180914	2016-09-20		Cumulative update package 14 (CU14) for SQL Server 2012 Service Pack 2\n" +
        "B	11.0.5655.0	SP2 CU13	CumulativeUpdate	3165266	2016-07-19		Cumulative update package 13 (CU13) for SQL Server 2012 Service Pack 2\n" +
        "B	11.0.5649.0	SP2 CU12	CumulativeUpdate	3152637	2016-05-16		Cumulative update package 12 (CU12) for SQL Server 2012 Service Pack 2\n" +
        "B	11.0.5646.0	SP2 CU11	CumulativeUpdate	3137745	2016-03-22		Cumulative update package 11 (CU11) for SQL Server 2012 Service Pack 2\n" +
        "B	11.0.5644.0	SP2 CU10	CumulativeUpdate	3120313	2016-01-20		Cumulative update package 10 (CU10) for SQL Server 2012 Service Pack 2\n" +
        "B	11.0.5641.0	SP2 CU9	CumulativeUpdate	3098512	2015-11-18		Cumulative update package 9 (CU9) for SQL Server 2012 Service Pack 2\n" +
        "B	11.0.5636.3	Hotfix	Hotfix	3097636	2015-09-22		FIX: Performance decrease when application with connection pooling frequently connects or disconnects in SQL Server\n" +
        "B	11.0.5634.0	SP2 CU8	CumulativeUpdate	3082561	2015-09-21		Cumulative update package 8 (CU8) for SQL Server 2012 Service Pack 2\n" +
        "B	11.0.5629.0	Hotfix	Hotfix	3087872	2015-08-31		FIX: Access violations when you use the FileTable feature in SQL Server 2012\n" +
        "B	11.0.5623.0	SP2 CU7	CumulativeUpdate	3072100	2015-07-20		Cumulative update package 7 (CU7) for SQL Server 2012 Service Pack 2\n" +
        "B	11.0.5613.0	SP2	SecurityUpdate	3045319	2015-07-14		MS15-058: Description of the security update for SQL Server 2012 Service Pack 2 QFE: July 14, 2015\n" +
        "B	11.0.5592.0	SP2 CU6	CumulativeUpdate	3052468	2015-05-19		Cumulative update package 6 (CU6) for SQL Server 2012 Service Pack 2\n" +
        "B	11.0.5582.0	SP2 CU5	CumulativeUpdate	3037255	2015-03-16		Cumulative update package 5 (CU5) for SQL Server 2012 Service Pack 2\n" +
        "B	11.0.5571.0	Hotfix	Hotfix	3034679	2015-05-15		FIX: AlwaysOn availability groups are reported as NOT SYNCHRONIZING\n" +
        "B	11.0.5569.0	SP2 CU4	CumulativeUpdate	3007556	2015-01-20		Cumulative update package 4 (CU4) for SQL Server 2012 Service Pack 2\n" +
        "B	11.0.5556.0	SP2 CU3	CumulativeUpdate	3002049	2014-11-17		Cumulative update package 3 (CU3) for SQL Server 2012 Service Pack 2\n" +
        "B	11.0.5548.0	SP2 CU2	CumulativeUpdate	2983175	2014-09-15		Cumulative update package 2 (CU2) for SQL Server 2012 Service Pack 2\n" +
        "B	11.0.5532.0	SP2 CU1	CumulativeUpdate	2976982	2014-07-24		Cumulative update package 1 (CU1) for SQL Server 2012 Service Pack 2\n" +
        "B	11.0.5522.0	Hotfix	Hotfix	2969896	2014-06-20		FIX: Data loss in clustered index occurs when you run online build index in SQL Server 2012 (Hotfix for SQL2012 SP2)\n" +
        "B	11.0.5388.0	RTM + security update	SecurityUpdate	3194719	2016-11-08		MS16-136: Description of the security update for SQL Server 2012 Service Pack 2 GDR: November 8, 2016\n" +
        "B	11.0.5352.0	GDR	SecurityUpdate	3135244	2016-01-27		TLS 1.2 support for SQL Server 2012 SP2 GDR\n" +
        "B	11.0.5343.0	RTM + security update	SecurityUpdate	3045321	2015-07-14		MS15-058: Description of the security update for SQL Server 2012 Service Pack 2 GDR: July 14, 2015\n" +
        "B	11.0.5058.0	SP2	ServicePack		2014-06-10		SQL Server 2012 Service Pack 2 (SP2)\n" +
        "B	11.0.3513.0	SP1 + security update	SecurityUpdate	3045317	2015-07-14		MS15-058: Description of the security update for SQL Server 2012 SP1 QFE: July 14, 2015\n" +
        "B	11.0.3492.0	SP1 CU16	CumulativeUpdate	3052476	2015-05-18		Cumulative update package 16 (CU16) for SQL Server 2012 Service Pack 1\n" +
        "B	11.0.3487.0	SP1 CU15	CumulativeUpdate	3038001	2015-03-16		Cumulative update package 15 (CU15) for SQL Server 2012 Service Pack 1\n" +
        "B	11.0.3486.0	SP1 CU14	CumulativeUpdate	3023636	2015-01-19		Cumulative update package 14 (CU14) for SQL Server 2012 Service Pack 1\n" +
        "B	11.0.3482.0	SP1 CU13	CumulativeUpdate	3002044	2014-11-17		Cumulative update package 13 (CU13) for SQL Server 2012 Service Pack 1\n" +
        "B	11.0.3470.0	SP1 CU12	CumulativeUpdate	2991533	2014-09-15		Cumulative update package 12 (CU12) for SQL Server 2012 Service Pack 1\n" +
        "B	11.0.3460.0	SP1	SecurityUpdate	2977325	2014-08-12		MS14-044: Description of the security update for SQL Server 2012 Service Pack 1 (QFE)\n" +
        "B	11.0.3449.0	SP1 CU11	CumulativeUpdate	2975396	2014-07-21		Cumulative update package 11 (CU11) for SQL Server 2012 Service Pack 1\n" +
        "B	11.0.3437.0	Hotfix	Hotfix	2969896	2014-06-10		FIX: Data loss in clustered index occurs when you run online build index in SQL Server 2012 (Hotfix for SQL2012 SP1)\n" +
        "B	11.0.3431.0	SP1 CU10	CumulativeUpdate	2954099	2014-05-19		Cumulative update package 10 (CU10) for SQL Server 2012 Service Pack 1\n" +
        "B	11.0.3412.0	SP1 CU9	CumulativeUpdate	2931078	2014-03-18		Cumulative update package 9 (CU9) for SQL Server 2012 Service Pack 1\n" +
        "B	11.0.3401.0	SP1 CU8	CumulativeUpdate	2917531	2014-01-20		Cumulative update package 8 (CU8) for SQL Server 2012 Service Pack 1\n" +
        "B	11.0.3393.0	SP1 CU7	CumulativeUpdate	2894115	2013-11-18		Cumulative update package 7 (CU7) for SQL Server 2012 Service Pack 1\n" +
        "B	11.0.3381.0	SP1 CU6	CumulativeUpdate	2874879	2013-09-16		Cumulative update package 6 (CU6) for SQL Server 2012 Service Pack 1\n" +
        "B	11.0.3373.0	SP1 CU5	CumulativeUpdate	2861107	2013-07-16		Cumulative update package 5 (CU5) for SQL Server 2012 Service Pack 1\n" +
        "B	11.0.3368.0	SP1 CU4	CumulativeUpdate	2833645	2013-05-31		Cumulative update package 4 (CU4) for SQL Server 2012 Service Pack 1\n" +
        "B	11.0.3350.0	Hotfix	Hotfix	2832017	2013-04-17		FIX: You can’t create or open SSIS projects or maintenance plans after you apply Cumulative Update 3 for SQL Server 2012 SP1\n" +
        "B	11.0.3349.0	SP1 CU3	CumulativeUpdate	2812412	2013-03-18		Cumulative update package 3 (CU3) for SQL Server 2012 Service Pack 1\n" +
        "B	11.0.3339.0	SP1 CU2	CumulativeUpdate	2790947	2013-01-25		Cumulative update package 2 (CU2) for SQL Server 2012 Service Pack 1\n" +
        "B	11.0.3335.0	Hotfix	Hotfix	2800050	2013-01-14		FIX: Component installation process fails after you install SQL Server 2012 SP1\n" +
        "B	11.0.3321.0	SP1 CU1	CumulativeUpdate	2765331	2012-11-20		Cumulative update package 1 (CU1) for SQL Server 2012 Service Pack 1\n" +
        "B	11.0.3156.0	SP1 + security update	SecurityUpdate	3045318	2015-07-14		MS15-058: Description of the security update for SQL Server 2012 SP1 GDR: July 14, 2015\n" +
        "B	11.0.3153.0	RTM + security update	SecurityUpdate	2977326	2014-08-12		MS14-044: Description of the security update for SQL Server 2012 Service Pack 1 (GDR)\n" +
        "B	11.0.3128.0		Unknown	2793634	2013-01-03		Windows Installer starts repeatedly after you install SQL Server 2012 SP1\n" +
        "B	11.0.3000.0	SP1	ServicePack		2012-11-06		SQL Server 2012 Service Pack 1 (SP1)\n" +
        "B	11.0.2845.0	CTP 4	Preview		2012-09-20		SQL Server 2012 Service Pack 1 Customer Technology Preview 4 (CTP4) Beta\n" +
        "B	11.0.2809.24	CTP 3	Preview		2012-07-05		SQL Server 2012 Service Pack 1 Customer Technology Preview 3 (CTP3) Beta\n" +
        "B	11.0.2424.0	CU11	CumulativeUpdate	2908007	2013-12-17		Cumulative update package 11 (CU11) for SQL Server 2012\n" +
        "B	11.0.2420.0	CU10	CumulativeUpdate	2891666	2013-10-21		Cumulative update package 10 (CU10) for SQL Server 2012\n" +
        "B	11.0.2419.0	CU9	CumulativeUpdate	2867319	2013-08-21		Cumulative update package 9 (CU9) for SQL Server 2012\n" +
        "B	11.0.2410.0	CU8	CumulativeUpdate	2844205	2013-06-18		Cumulative update package 8 (CU8) for SQL Server 2012\n" +
        "B	11.0.2405.0	CU7	CumulativeUpdate	2823247	2013-04-15		Cumulative update package 7 (CU7) for SQL Server 2012\n" +
        "B	11.0.2401.0	CU6	CumulativeUpdate	2728897	2013-02-18		Cumulative update package 6 (CU6) for SQL Server 2012\n" +
        "B	11.0.2395.0	CU5	CumulativeUpdate	2777772	2012-12-18		Cumulative update package 5 (CU5) for SQL Server 2012\n" +
        "B	11.0.2383.0	CU4	CumulativeUpdate	2758687	2012-10-18		Cumulative update package 4 (CU4) for SQL Server 2012\n" +
        "B	11.0.2376.0		Unknown		2012-10-09		Microsoft Security Bulletin MS12-070\n" +
        "B	11.0.2332.0	CU3	CumulativeUpdate	2723749	2012-08-29		Cumulative update package 3 (CU3) for SQL Server 2012\n" +
        "B	11.0.2325.0	CU2	CumulativeUpdate	2703275	2012-06-18		Cumulative update package 2 (CU2) for SQL Server 2012\n" +
        "B	11.0.2318.0	RTM	Rtm		2012-04-19		SQL Server 2012 Express LocalDB RTM\n" +
        "B	11.0.2316.0	CU1	CumulativeUpdate	2679368	2012-04-12		Cumulative update package 1 (CU1) for SQL Server 2012\n" +
        "B	11.0.2218.0		Unknown		2012-10-09		Microsoft Security Bulletin MS12-070\n" +
        "B	11.0.2214.0	Hotfix	Hotfix	2685308	2012-04-06		FIX: SSAS uses only 20 cores in SQL Server 2012 Business Intelligence\n" +
        "B	11.0.2100.60	RTM	Rtm		2012-03-06		SQL Server 2012 RTM\n" +
        "B	11.0.1913.37	RC1	Preview		2011-12-16		Microsoft SQL Server 2012 Release Candidate 1 (RC1) Beta\n" +
        "B	11.0.1750.32	RC0	Preview		2011-11-17		Microsoft SQL Server 2012 Release Candidate 0 (RC0) Beta\n" +
        "B	11.0.1440.19	CTP 3	Preview		2011-07-11		Microsoft SQL Server 2012 (codename Denali) Community Technology Preview 3 (CTP3) Beta\n" +
        "B	11.0.1103.9	CTP 1	Preview		2010-11-08		Microsoft SQL Server 2012 (codename Denali) Community Technology Preview 1 (CTP1) Beta\n" +
        "R	10.50	SQL Server 2008 R2	Kilimanjaro	10.50.1600.1	2010-04-21	2014-07-08	2019-07-09\n" +
        "B	10.50.6785.2	SP3 + security update	SecurityUpdate	5021112	2023-02-14		Security update for SQL Server 2008 R2 SP3 GDR: February 14, 2023\n" +
        "B	10.50.6560.0	SP3 + security update	SecurityUpdate	4057113	2018-01-06		Security update for SQL Server 2008 R2 SP3 GDR: January 6, 2018 – Security Advisory ADV180002\n" +
        "B	10.50.6549.0		Unknown				An unknown but existing build\n" +
        "B	10.50.6542.0		Unknown	3146034	2016-03-03		Intermittent service terminations occur after you install any SQL Server 2008 or SQL Server 2008 R2 versions from KB3135244\n" +
        "B	10.50.6537.0		Unknown	3135244	2016-01-27		TLS 1.2 support for SQL Server 2008 R2 SP3\n" +
        "B	10.50.6529.0	SP3	SecurityUpdate	3045314	2015-07-14		MS15-058: Description of the security update for SQL Server 2008 R2 Service Pack 3 QFE: July 14, 2015\n" +
        "B	10.50.6525.0	SP3	Hotfix	3033860	2015-02-09		An on-demand hotfix update package is available for SQL Server 2008 R2 Service Pack 3 (SP3)\n" +
        "B	10.50.6220.0	RTM + security update	SecurityUpdate	3045316	2015-07-14		MS15-058: Description of the security update for SQL Server 2008 R2 Service Pack 3 GDR: July 14, 2015\n" +
        "B	10.50.6000.34	SP3	ServicePack		2014-09-26		SQL Server 2008 R2 Service Pack 3 (SP3)\n" +
        "B	10.50.4343.0		Unknown	3135244	2016-01-27		TLS 1.2 support for SQL Server 2008 R2 SP2 (IA-64 only)\n" +
        "B	10.50.4339.0	SP2	SecurityUpdate	3045312	2015-07-14		MS15-058: Description of the security update for SQL Server 2008 R2 Service Pack 2 QFE: July 14, 2015\n" +
        "B	10.50.4331.0		Unknown	2987585	2014-08-27		Restore Log with Standby Mode on an Advanced Format disk may cause a 9004 error in SQL Server 2008 R2 or SQL Server 2012\n" +
        "B	10.50.4321.0	SP2	SecurityUpdate	2977319	2014-08-12		MS14-044: Description of the security update for SQL Server 2008 R2 Service Pack 2 (QFE)\n" +
        "B	10.50.4319.0	SP2 CU13	CumulativeUpdate	2967540	2014-06-30		Cumulative update package 13 (CU13) for SQL Server 2008 R2 Service Pack 2\n" +
        "B	10.50.4305.0	SP2 CU12	CumulativeUpdate	2938478	2014-04-21		Cumulative update package 12 (CU12) for SQL Server 2008 R2 Service Pack 2\n" +
        "B	10.50.4302.0	SP2 CU11	CumulativeUpdate	2926028	2014-02-18		Cumulative update package 11 (CU11) for SQL Server 2008 R2 Service Pack 2\n" +
        "B	10.50.4297.0	SP2 CU10	CumulativeUpdate	2908087	2013-12-16		Cumulative update package 10 (CU10) for SQL Server 2008 R2 Service Pack 2\n" +
        "B	10.50.4295.0	SP2 CU9	CumulativeUpdate	2887606	2013-10-29		Cumulative update package 9 (CU9) for SQL Server 2008 R2 Service Pack 2\n" +
        "B	10.50.4290.0	SP2 CU8	CumulativeUpdate	2871401	2013-08-30		Cumulative update package 8 (CU8) for SQL Server 2008 R2 Service Pack 2\n" +
        "B	10.50.4286.0	SP2 CU7	CumulativeUpdate	2844090	2013-06-17		Cumulative update package 7 (CU7) for SQL Server 2008 R2 Service Pack 2\n" +
        "B	10.50.4285.0	SP2 CU6	CumulativeUpdate	2830140	2013-06-13		Cumulative update package 6 (CU6) for SQL Server 2008 R2 Service Pack 2 (updated)\n" +
        "B	10.50.4279.0	SP2 CU6	CumulativeUpdate		2013-04-15	1	Cumulative update package 6 (CU6) for SQL Server 2008 R2 Service Pack 2 (replaced)\n" +
        "B	10.50.4276.0	SP2 CU5	CumulativeUpdate	2797460	2013-02-18		Cumulative update package 5 (CU5) for SQL Server 2008 R2 Service Pack 2\n" +
        "B	10.50.4270.0	SP2 CU4	CumulativeUpdate	2777358	2012-12-17		Cumulative update package 4 (CU4) for SQL Server 2008 R2 Service Pack 2\n" +
        "B	10.50.4266.0	SP2 CU3	CumulativeUpdate	2754552	2012-10-15		Cumulative update package 3 (CU3) for SQL Server 2008 R2 Service Pack 2\n" +
        "B	10.50.4263.0	SP2 CU2	CumulativeUpdate	2740411	2012-08-29		Cumulative update package 2 (CU2) for SQL Server 2008 R2 Service Pack 2\n" +
        "B	10.50.4260.0	SP2 CU1	CumulativeUpdate	2720425	2012-08-01		Cumulative update package 1 (CU1) for SQL Server 2008 R2 Service Pack 2\n" +
        "B	10.50.4046.0	GDR	SecurityUpdate	3135244	2016-01-27		TLS 1.2 support for SQL Server 2008 R2 SP2 GDR (IA-64 only)\n" +
        "B	10.50.4042.0	RTM + security update	SecurityUpdate	3045313	2015-07-14		MS15-058: Description of the security update for SQL Server 2008 R2 Service Pack 2 GDR: July 14, 2015\n" +
        "B	10.50.4033.0	RTM + security update	SecurityUpdate	2977320	2014-08-12		MS14-044: Description of the security update for SQL Server 2008 R2 Service Pack 2 (GDR)\n" +
        "B	10.50.4000.0	SP2	ServicePack		2012-07-26		SQL Server 2008 R2 Service Pack 2 (SP2)\n" +
        "B	10.50.3720.0	CTP	Preview		2012-05-13		SQL Server 2008 R2 Service Pack 2 Community Technology Preview (CTP) Beta\n" +
        "B	10.50.2881.0	SP1	Hotfix	2868244	2013-08-12		An on-demand hotfix update package for SQL Server 2008 R2 Service Pack 1\n" +
        "B	10.50.2876.0	SP1 CU13	CumulativeUpdate	2855792	2013-06-17		Cumulative update package 13 (CU13) for SQL Server 2008 R2 Service Pack 1\n" +
        "B	10.50.2875.0	SP1 CU12	CumulativeUpdate	2828727	2013-06-13		Cumulative update package 12 (CU12) for SQL Server 2008 R2 Service Pack 1 (updated)\n" +
        "B	10.50.2874.0	SP1 CU12	CumulativeUpdate		2013-04-15	1	Cumulative update package 12 (CU12) for SQL Server 2008 R2 Service Pack 1 (replaced)\n" +
        "B	10.50.2869.0	SP1 CU11	CumulativeUpdate	2812683	2013-02-18		Cumulative update package 11 (CU11) for SQL Server 2008 R2 Service Pack 1\n" +
        "B	10.50.2868.0	SP1 CU10	CumulativeUpdate	2783135	2012-12-17		Cumulative update package 10 (CU10) for SQL Server 2008 R2 Service Pack 1\n" +
        "B	10.50.2866.0	SP1 CU9	CumulativeUpdate	2756574	2012-11-06		Cumulative update package 9 (CU9) for SQL Server 2008 R2 Service Pack 1\n" +
        "B	10.50.2861.0		Unknown		2012-10-09		Microsoft Security Bulletin MS12-070\n" +
        "B	10.50.2861.0	SP1	SecurityUpdate	2716439	2012-10-09		MS12-070: Description of the security update for SQL Server 2008 R2 Service Pack 1 QFE: October 9, 2012\n" +
        "B	10.50.2822.0	SP1 CU8	CumulativeUpdate	2723743	2012-08-29		Cumulative update package 8 (CU8) for SQL Server 2008 R2 Service Pack 1\n" +
        "B	10.50.2817.0	SP1 CU7	CumulativeUpdate	2703282	2012-06-18		Cumulative update package 7 (CU7) for SQL Server 2008 R2 Service Pack 1\n" +
        "B	10.50.2811.0	SP1 CU6	CumulativeUpdate	2679367	2012-04-16		Cumulative update package 6 (CU6) for SQL Server 2008 R2 Service Pack 1\n" +
        "B	10.50.2807.0	Hotfix	Hotfix	2675522	2012-03-12		FIX: Access violation when you run DML statements against a table that has partitioned indexes in SQL Server 2008 R2\n" +
        "B	10.50.2806.0	SP1 CU5	CumulativeUpdate	2659694	2012-02-22		Cumulative update package 5 (CU5) for SQL Server 2008 R2 Service Pack 1\n" +
        "B	10.50.2799.0	Hotfix	Hotfix	2633357	2012-02-22		FIX: \"Non-yielding Scheduler\" error might occur when you run a query that uses the CHARINDEX function in SQL Server 2008 R2\n" +
        "B	10.50.2796.0	SP1 CU4	CumulativeUpdate	2633146	2011-12-20		Cumulative update package 4 (CU4) for SQL Server 2008 R2 Service Pack 1\n" +
        "B	10.50.2789.0	SP1 CU3	CumulativeUpdate	2591748	2011-10-17		Cumulative update package 3 (CU3) for SQL Server 2008 R2 Service Pack 1\n" +
        "B	10.50.2776.0	Hotfix	Hotfix	2606883	2011-10-18		FIX: Slow performance when an AFTER trigger runs on a partitioned table in SQL Server 2008 R2\n" +
        "B	10.50.2772.0	SP1 CU2	CumulativeUpdate	2567714	2011-08-15		Cumulative update package 2 (CU2) for SQL Server 2008 R2 Service Pack 1\n" +
        "B	10.50.2769.0	SP1 CU1	CumulativeUpdate	2544793	2011-07-18		Cumulative update package 1 (CU1) for SQL Server 2008 R2 Service Pack 1\n" +
        "B	10.50.2550.0		Unknown		2012-10-09		Microsoft Security Bulletin MS12-070\n" +
        "B	10.50.2500.0	SP1	ServicePack		2011-07-11		SQL Server 2008 R2 Service Pack 1 (SP1)\n" +
        "B	10.50.1817.0	CU14	CumulativeUpdate	2703280	2012-06-18		Cumulative update package 14 (CU14) for SQL Server 2008 R2\n" +
        "B	10.50.1815.0	CU13	CumulativeUpdate	2679366	2012-04-17		Cumulative update package 13 (CU13) for SQL Server 2008 R2\n" +
        "B	10.50.1810.0	CU12	CumulativeUpdate	2659692	2012-02-21		Cumulative update package 12 (CU12) for SQL Server 2008 R2\n" +
        "B	10.50.1809.0	CU11	CumulativeUpdate	2633145	2012-01-09		Cumulative update package 11 (CU11) for SQL Server 2008 R2\n" +
        "B	10.50.1807.0	CU10	CumulativeUpdate	2591746	2011-10-19		Cumulative update package 10 (CU10) for SQL Server 2008 R2\n" +
        "B	10.50.1804.0	CU9	CumulativeUpdate	2567713	2011-08-16		Cumulative update package 9 (CU9) for SQL Server 2008 R2\n" +
        "B	10.50.1800.0	Hotfix	Hotfix	2574699	2011-10-18		FIX: Database data files might be incorrectly marked as sparse in SQL Server 2008 R2 or in SQL Server 2008 even when the physical files are marked as not sparse in the file system\n" +
        "B	10.50.1797.0	CU8	CumulativeUpdate	2534352	2011-06-20		Cumulative update package 8 (CU8) for SQL Server 2008 R2\n" +
        "B	10.50.1790.0		SecurityUpdate	2494086	2011-06-17		MS11-049: Description of the security update for SQL Server 2008 R2 QFE: June 14, 2011\n" +
        "B	10.50.1777.0	CU7	CumulativeUpdate	2507770	2011-06-16		Cumulative update package 7 (CU7) for SQL Server 2008 R2\n" +
        "B	10.50.1769.0	Hotfix	Hotfix	2520808	2011-04-18		FIX: Non-yielding scheduler error when you run a query that uses a TVP in SQL Server 2008 or in SQL Server 2008 R2 if SQL Profiler or SQL Server Extended Events is used\n" +
        "B	10.50.1765.0	CU6	CumulativeUpdate	2489376	2011-02-21		Cumulative update package 6 (CU6) for SQL Server 2008 R2\n" +
        "B	10.50.1753.0	CU5	CumulativeUpdate	2438347	2010-12-23		Cumulative update package 5 (CU5) for SQL Server 2008 R2\n" +
        "B	10.50.1746.0	CU4	CumulativeUpdate	2345451	2010-10-18		Cumulative update package 4 (CU4) for SQL Server 2008 R2\n" +
        "B	10.50.1734.0	CU3	CumulativeUpdate	2261464	2010-08-20		Cumulative update package 3 (CU3) for SQL Server 2008 R2\n" +
        "B	10.50.1720.0	CU2	CumulativeUpdate	2072493	2010-06-25		Cumulative update package 2 (CU2) for SQL Server 2008 R2\n" +
        "B	10.50.1702.0	CU1	CumulativeUpdate	981355	2010-05-18		Cumulative update package 1 (CU1) for SQL Server 2008 R2\n" +
        "B	10.50.1617.0	RTM + security update	SecurityUpdate	2494088	2011-06-14		MS11-049: Description of the security update for SQL Server 2008 R2 GDR: June 14, 2011\n" +
        "B	10.50.1600.1	RTM	Rtm		2010-04-21		SQL Server 2008 R2 RTM\n" +
        "B	10.50.1352.12	CTP	Preview		2009-11-12		Microsoft SQL Server 2008 R2 November Community Technology Preview (CTP) Beta\n" +
        "B	10.50.1092.20	CTP	Preview		2009-06-30		Microsoft SQL Server 2008 R2 August Community Technology Preview (CTP) Beta\n" +
        "R	10.0	SQL Server 2008	Katmai	10.0.1600.22	2008-08-07	2014-07-08	2019-07-09\n" +
        "B	10.0.6814.4	SP4 + security update	SecurityUpdate	5020863	2023-02-14		Security update for SQL Server 2008 SP4 GDR: February 14, 2023\n" +
        "B	10.0.6556.0	SP4 + security update	SecurityUpdate	4057114	2018-01-06		Security update for SQL Server 2008 SP4 GDR: January 6, 2018 – Security Advisory ADV180002\n" +
        "B	10.0.6547.0		Unknown	3146034	2016-03-03		Intermittent service terminations occur after you install any SQL Server 2008 or SQL Server 2008 R2 versions from KB3135244\n" +
        "B	10.0.6543.0		Unknown	3135244	2016-01-27		TLS 1.2 support for SQL Server 2008 SP4\n" +
        "B	10.0.6535.0	SP4	SecurityUpdate	3045308	2015-07-14		MS15-058: Description of the security update for SQL Server 2008 Service Pack 4 QFE: July 14, 2015\n" +
        "B	10.0.6526.0	SP4	Hotfix	3034373	2015-02-09		An on-demand hotfix update package is available for SQL Server 2008 Service Pack 4 (SP4)\n" +
        "B	10.0.6241.0	RTM + security update	SecurityUpdate	3045311	2015-07-14		MS15-058: Description of the security update for SQL Server 2008 Service Pack 4 GDR: July 14, 2015\n" +
        "B	10.0.6000.29	SP4	ServicePack		2014-09-30		SQL Server 2008 Service Pack 4 (SP4)\n" +
        "B	10.0.5894.0		Unknown	3135244	2016-01-27		TLS 1.2 support for SQL Server 2008 SP3 (IA-64 only)\n" +
        "B	10.0.5890.0	SP3	SecurityUpdate	3045303	2015-07-14		MS15-058: Description of the security update for SQL Server 2008 Service Pack 3 QFE: July 14, 2015\n" +
        "B	10.0.5869.0	SP3 + security update	SecurityUpdate	2977322	2014-08-12		MS14-044: Description of the security update for SQL Server 2008 SP3 (QFE)\n" +
        "B	10.0.5867.0	Hotfix	Hotfix	2877204	2014-07-02		FIX: Error 8985 when you run the \"dbcc shrinkfile\" statement by using the logical name of a file in SQL Server 2008 R2 or SQL Server 2008\n" +
        "B	10.0.5861.0	SP3 CU17	CumulativeUpdate	2958696	2014-05-19		Cumulative update package 17 (CU17) for SQL Server 2008 Service Pack 3\n" +
        "B	10.0.5852.0	SP3 CU16	CumulativeUpdate	2936421	2014-03-17		Cumulative update package 16 (CU16) for SQL Server 2008 Service Pack 3\n" +
        "B	10.0.5850.0	SP3 CU15	CumulativeUpdate	2923520	2014-01-20		Cumulative update package 15 (CU15) for SQL Server 2008 Service Pack 3\n" +
        "B	10.0.5848.0	SP3 CU14	CumulativeUpdate	2893410	2013-11-18		Cumulative update package 14 (CU14) for SQL Server 2008 Service Pack 3\n" +
        "B	10.0.5846.0	SP3 CU13	CumulativeUpdate	2880350	2013-09-16		Cumulative update package 13 (CU13) for SQL Server 2008 Service Pack 3\n" +
        "B	10.0.5844.0	SP3 CU12	CumulativeUpdate	2863205	2013-07-16		Cumulative update package 12 (CU12) for SQL Server 2008 Service Pack 3\n" +
        "B	10.0.5841.0	SP3 CU11	CumulativeUpdate	2834048	2013-06-13		Cumulative update package 11 (CU11) for SQL Server 2008 Service Pack 3 (updated)\n" +
        "B	10.0.5840.0	SP3 CU11	CumulativeUpdate		2013-05-20	1	Cumulative update package 11 (CU11) for SQL Server 2008 Service Pack 3 (replaced)\n" +
        "B	10.0.5835.0	SP3 CU10	CumulativeUpdate	2814783	2013-03-18		Cumulative update package 10 (CU10) for SQL Server 2008 Service Pack 3\n" +
        "B	10.0.5829.0	SP3 CU9	CumulativeUpdate	2799883	2013-01-23		Cumulative update package 9 (CU9) for SQL Server 2008 Service Pack 3\n" +
        "B	10.0.5828.0	SP3 CU8	CumulativeUpdate	2771833	2012-11-19		Cumulative update package 8 (CU8) for SQL Server 2008 Service Pack 3\n" +
        "B	10.0.5826.0		Unknown	2716435	2012-10-09		Microsoft Security Bulletin MS12-070\n" +
        "B	10.0.5794.0	SP3 CU7	CumulativeUpdate	2738350	2012-09-21		Cumulative update package 7 (CU7) for SQL Server 2008 Service Pack 3\n" +
        "B	10.0.5788.0	SP3 CU6	CumulativeUpdate	2715953	2012-07-16		Cumulative update package 6 (CU6) for SQL Server 2008 Service Pack 3\n" +
        "B	10.0.5785.0	SP3 CU5	CumulativeUpdate	2696626	2012-05-19		Cumulative update package 5 (CU5) for SQL Server 2008 Service Pack 3\n" +
        "B	10.0.5775.0	SP3 CU4	CumulativeUpdate	2673383	2012-03-20		Cumulative update package 4 (CU4) for SQL Server 2008 Service Pack 3\n" +
        "B	10.0.5770.0	SP3 CU3	CumulativeUpdate	2648098	2012-01-16		Cumulative update package 3 (CU3) for SQL Server 2008 Service Pack 3\n" +
        "B	10.0.5768.0	SP3 CU2	CumulativeUpdate	2633143	2011-11-22		Cumulative update package 2 (CU2) for SQL Server 2008 Service Pack 3\n" +
        "B	10.0.5766.0	SP3 CU1	CumulativeUpdate	2617146	2011-10-18		Cumulative update package 1 (CU1) for SQL Server 2008 Service Pack 3\n" +
        "B	10.0.5544.0	GDR	SecurityUpdate	3135244	2016-01-27		TLS 1.2 support for SQL Server 2008 SP3 GDR (IA-64 only)\n" +
        "B	10.0.5538.0	RTM + security update	SecurityUpdate	3045305	2015-07-14		MS15-058: Description of the security update for SQL Server 2008 Service Pack 3 GDR: July 14, 2015\n" +
        "B	10.0.5520.0	SP3 + security update	SecurityUpdate	2977321	2014-08-12		MS14-044: Description of the security update for SQL Server 2008 SP3 (GDR)\n" +
        "B	10.0.5512.0		Unknown		2012-10-09		Microsoft Security Bulletin MS12-070\n" +
        "B	10.0.5500.0	SP3	ServicePack		2011-10-06		SQL Server 2008 Service Pack 3 (SP3)\n" +
        "B	10.0.5416.0	CTP	Preview		2011-08-22		SQL Server 2008 Service Pack 3 CTP Beta\n" +
        "B	10.0.4371.0		Unknown		2012-10-09		Microsoft Security Bulletin MS12-070\n" +
        "B	10.0.4333.0	SP2 CU11	CumulativeUpdate	2715951	2012-07-16		Cumulative update package 11 (CU11) for SQL Server 2008 Service Pack 2\n" +
        "B	10.0.4332.0	SP2 CU10	CumulativeUpdate	2696625	2012-05-20		Cumulative update package 10 (CU10) for SQL Server 2008 Service Pack 2\n" +
        "B	10.0.4330.0	SP2 CU9	CumulativeUpdate	2673382	2012-03-19		Cumulative update package 9 (CU9) for SQL Server 2008 Service Pack 2\n" +
        "B	10.0.4326.0	SP2 CU8	CumulativeUpdate	2648096	2012-01-30		Cumulative update package 8 (CU8) for SQL Server 2008 Service Pack 2\n" +
        "B	10.0.4323.0	SP2 CU7	CumulativeUpdate	2617148	2011-11-21		Cumulative update package 7 (CU7) for SQL Server 2008 Service Pack 2\n" +
        "B	10.0.4321.0	SP2 CU6	CumulativeUpdate	2582285	2011-09-20		Cumulative update package 6 (CU6) for SQL Server 2008 Service Pack 2\n" +
        "B	10.0.4316.0	SP2 CU5	CumulativeUpdate	2555408	2011-07-18		Cumulative update package 5 (CU5) for SQL Server 2008 Service Pack 2\n" +
        "B	10.0.4285.0	SP2 CU4	CumulativeUpdate	2527180	2011-05-16		Cumulative update package 4 (CU4) for SQL Server 2008 Service Pack 2\n" +
        "B	10.0.4279.0	SP2 CU3	CumulativeUpdate	2498535	2011-03-11		Cumulative update package 3 (CU3) for SQL Server 2008 Service Pack 2\n" +
        "B	10.0.4272.0	SP2 CU2	CumulativeUpdate	2467239	2011-02-10		Cumulative update package 2 (CU2) for SQL Server 2008 Service Pack 2\n" +
        "B	10.0.4266.0	SP2 CU1	CumulativeUpdate	2289254	2010-11-15		Cumulative update package 1 (CU1) for SQL Server 2008 Service Pack 2\n" +
        "B	10.0.4067.0		Unknown		2012-10-09		Microsoft Security Bulletin MS12-070\n" +
        "B	10.0.4064.0	RTM + security update	SecurityUpdate	2494089	2011-06-14		MS11-049: Description of the security update for SQL Server 2008 Service Pack 2 GDR: June 14, 2011\n" +
        "B	10.0.4000.0	SP2	ServicePack		2010-09-29		SQL Server 2008 Service Pack 2 (SP2)\n" +
        "B	10.0.3798.0	CTP	Preview		2010-07-07		SQL Server 2008 Service Pack 2 CTP Beta\n" +
        "B	10.0.2850.0	SP1 CU16	CumulativeUpdate	2582282	2011-09-19		Cumulative update package 16 (CU16) for SQL Server 2008 Service Pack 1\n" +
        "B	10.0.2847.0	SP1 CU15	CumulativeUpdate	2555406	2011-07-18		Cumulative update package 15 (CU15) for SQL Server 2008 Service Pack 1\n" +
        "B	10.0.2841.0	SP1	SecurityUpdate	2494100	2011-06-14		MS11-049: Description of the security update for SQL Server 2008 Service Pack 1 QFE: June 14, 2011\n" +
        "B	10.0.2821.0	SP1 CU14	CumulativeUpdate	2527187	2011-05-16		Cumulative update package 14 (CU14) for SQL Server 2008 Service Pack 1\n" +
        "B	10.0.2816.0	SP1 CU13	CumulativeUpdate	2497673	2011-03-22		Cumulative update package 13 (CU13) for SQL Server 2008 Service Pack 1\n" +
        "B	10.0.2808.0	SP1 CU12	CumulativeUpdate	2467236	2011-02-10		Cumulative update package 12 (CU12) for SQL Server 2008 Service Pack 1\n" +
        "B	10.0.2804.0	SP1 CU11	CumulativeUpdate	2413738	2010-11-15		Cumulative update package 11 (CU11) for SQL Server 2008 Service Pack 1\n" +
        "B	10.0.2799.0	SP1 CU10	CumulativeUpdate	2279604	2010-09-21		Cumulative update package 10 (CU10) for SQL Server 2008 Service Pack 1\n" +
        "B	10.0.2789.0	SP1 CU9	CumulativeUpdate	2083921	2010-07-21		Cumulative update package 9 (CU9) for SQL Server 2008 Service Pack 1\n" +
        "B	10.0.2787.0	Hotfix	Hotfix	2231277	2010-07-30		FIX: The Reporting Services service stops unexpectedly after you apply SQL Server 2008 SP1 CU 7 or CU8\n" +
        "B	10.0.2775.0	SP1 CU8	CumulativeUpdate	981702	2010-05-17		Cumulative update package 8 (CU8) for SQL Server 2008 Service Pack 1\n" +
        "B	10.0.2766.0	SP1 CU7	CumulativeUpdate	979065	2010-03-26		Cumulative update package 7 (CU7) for SQL Server 2008 Service Pack 1\n" +
        "B	10.0.2757.0	SP1 CU6	CumulativeUpdate	977443	2010-01-18		Cumulative update package 6 (CU6) for SQL Server 2008 Service Pack 1\n" +
        "B	10.0.2746.0	SP1 CU5	CumulativeUpdate	975977	2009-11-16		Cumulative update package 5 (CU5) for SQL Server 2008 Service Pack 1\n" +
        "B	10.0.2740.0	Hotfix	Hotfix	976761	2009-11-24		FIX: Error message when you perform a rolling upgrade in a SQL Server 2008 cluster : \"18401, Login failed for user SQLTEST\\AgentService. Reason: Server is in script upgrade mode. Only administrator can connect at this time.[SQLState 42000]\"\n" +
        "B	10.0.2734.0	SP1 CU4	CumulativeUpdate	973602	2009-09-22		Cumulative update package 4 (CU4) for SQL Server 2008 Service Pack 1\n" +
        "B	10.0.2723.0	SP1 CU3	CumulativeUpdate	971491	2009-07-21		Cumulative update package 3 (CU3) for SQL Server 2008 Service Pack 1\n" +
        "B	10.0.2714.0	SP1 CU2	CumulativeUpdate	970315	2009-05-18		Cumulative update package 2 (CU2) for SQL Server 2008 Service Pack 1\n" +
        "B	10.0.2712.0	Hotfix	Hotfix	970507	2009-07-21		FIX: Error message in SQL Server 2008 when you run an INSERT SELECT statement on a table: \"Violation of PRIMARY KEY constraint '<PrimaryKey>'. Cannot insert duplicate key in object '<TableName>'\"\n" +
        "B	10.0.2710.0	SP1 CU1	CumulativeUpdate	969099	2009-04-16		Cumulative update package 1 (CU1) for SQL Server 2008 Service Pack 1\n" +
        "B	10.0.2573.0	RTM + security update	SecurityUpdate	2494096	2011-06-14		MS11-049: Description of the security update for SQL Server 2008 Service Pack 1 GDR: June 14, 2011\n" +
        "B	10.0.2531.0	SP1	ServicePack		2009-04-07		SQL Server 2008 Service Pack 1 (SP1)\n" +
        "B	10.0.2520.0	CTP	Preview		2009-02-23		SQL Server 2008 Service Pack 1 - CTP Beta\n" +
        "B	10.0.1835.0	CU10	CumulativeUpdate	979064	2010-03-15		Cumulative update package 10 (CU10) for SQL Server 2008\n" +
        "B	10.0.1828.0	CU9	CumulativeUpdate	977444	2010-01-18		Cumulative update package 9 (CU9) for SQL Server 2008\n" +
        "B	10.0.1823.0	CU8	CumulativeUpdate	975976	2009-11-16		Cumulative update package 8 (CU8) for SQL Server 2008\n" +
        "B	10.0.1818.0	CU7	CumulativeUpdate	973601	2009-09-21		Cumulative update package 7 (CU7) for SQL Server 2008\n" +
        "B	10.0.1812.0	CU6	CumulativeUpdate	971490	2009-07-21		Cumulative update package 6 (CU6) for SQL Server 2008\n" +
        "B	10.0.1806.0	CU5	CumulativeUpdate	969531	2009-05-18		Cumulative update package 5 (CU5) for SQL Server 2008\n" +
        "B	10.0.1798.0	CU4	CumulativeUpdate	963036	2009-03-17		Cumulative update package 4 (CU4) for SQL Server 2008\n" +
        "B	10.0.1787.0	CU3	CumulativeUpdate	960484	2009-01-19		Cumulative update package 3 (CU3) for SQL Server 2008\n" +
        "B	10.0.1779.0	CU2	CumulativeUpdate	958186	2008-11-19		Cumulative update package 2 (CU2) for SQL Server 2008\n" +
        "B	10.0.1771.0	Hotfix	Hotfix	958611	2008-10-29		FIX: You may receive incorrect results when you run a query that references three or more tables in the FROM clause in SQL Server 2008\n" +
        "B	10.0.1763.0	CU1	CumulativeUpdate	956717	2008-10-28		Cumulative update package 1 (CU1) for SQL Server 2008\n" +
        "B	10.0.1750.0	Hotfix	Hotfix	956718	2008-08-25		FIX: A MERGE statement may not enforce a foreign key constraint when the statement updates a unique key column that is not part of a clustering key that has a single row as the update source in SQL Server 2008\n" +
        "B	10.0.1600.22	RTM	Rtm		2008-08-07		SQL Server 2008 RTM\n" +
        "B	10.0.1442.32		Unknown		2008-06-05		Microsoft SQL Server 2008 RC0\n" +
        "B	10.0.1300.13	CTP	Preview		2008-02-19		Microsoft SQL Server 2008 CTP, February 2008 Beta\n" +
        "B	10.0.1075.23	CTP	Preview		2007-11-18		Microsoft SQL Server 2008 CTP, November 2007 Beta\n" +
        "B	10.0.1049.14	CTP	Preview		2007-07-31		SQL Server 2008 CTP, July 2007 Beta\n" +
        "B	10.0.1019.17	CTP	Preview		2007-05-21		SQL Server 2008 CTP, June 2007 Beta\n" +
        "R	9.0	SQL Server 2005	Yukon	9.0.1399.06	2005-11-07	2011-04-12	2016-04-12\n" +
        "B	9.0.5324	SP4	SecurityUpdate	2716427	2012-10-09		MS12-070: Description of the security update for SQL Server 2005 Service Pack 4 QFE\n" +
        "B	9.0.5296	Hotfix	Hotfix	2615425	2011-10-24		FIX: \"Msg 7359\" error when a view uses another view in SQL Server 2005 if the schema version of a remote table is updated\n" +
        "B	9.0.5295	Hotfix	Hotfix	2598903	2012-05-21		FIX: SQL Server Agent job randomly stops when you schedule the job to run past midnight on specific days in SQL Server 2005, in SQL Server 2008 or in SQL Server 2008 R2\n" +
        "B	9.0.5294	Hotfix	Hotfix	2572407	2011-08-10		FIX: Error 5180 when you use the ONLINE option to rebuild an index in SQL Server 2005\n" +
        "B	9.0.5292	SP4	SecurityUpdate	2494123	2011-06-14		MS11-049: Description of the security update for SQL Server 2005 Service Pack 4 QFE: June 14, 2011\n" +
        "B	9.0.5266	SP4 CU3	CumulativeUpdate	2507769	2011-03-22		Cumulative update package 3 (CU3) for SQL Server 2005 Service Pack 4\n" +
        "B	9.0.5259	SP4 CU2	CumulativeUpdate	2489409	2011-02-22		Cumulative update package 2 (CU2) for SQL Server 2005 Service Pack 4\n" +
        "B	9.0.5254	SP4 CU1	CumulativeUpdate	2464079	2010-12-24		Cumulative update package 1 (CU1) for SQL Server 2005 Service Pack 4\n" +
        "B	9.0.5069		Unknown		2012-10-09		Microsoft Security Bulletin MS12-070\n" +
        "B	9.0.5057	RTM + security update	SecurityUpdate	2494120	2011-06-14		MS11-049: Description of the security update for SQL Server 2005 Service Pack 4 GDR: June 14, 2011\n" +
        "B	9.0.5000	SP4	ServicePack	2463332	2010-12-17		SQL Server 2005 Service Pack 4 (SP4)\n" +
        "B	9.0.4912	CTP	Preview		2010-11-03		SQL Server 2005 Service Pack 4 (SP4) - Customer Technology Preview (CTP) Beta\n" +
        "B	9.0.4342	Hotfix	Hotfix	2598903	2012-05-21		FIX: SQL Server Agent job randomly stops when you schedule the job to run past midnight on specific days in SQL Server 2005, in SQL Server 2008 or in SQL Server 2008 R2\n" +
        "B	9.0.4340	SP3	SecurityUpdate	2494112	2011-06-14		MS11-049: Description of the security update for SQL Server 2005 Service Pack 3 QFE: June 14, 2011\n" +
        "B	9.0.4325	SP3 CU15	CumulativeUpdate	2507766	2011-03-22		Cumulative update package 15 (CU15) for SQL Server 2005 Service Pack 3\n" +
        "B	9.0.4317	SP3 CU14	CumulativeUpdate	2489375	2011-02-21		Cumulative update package 14 (CU14) for SQL Server 2005 Service Pack 3\n" +
        "B	9.0.4315	SP3 CU13	CumulativeUpdate	2438344	2010-12-23		Cumulative update package 13 (CU13) for SQL Server 2005 Service Pack 3\n" +
        "B	9.0.4311	SP3 CU12	CumulativeUpdate	2345449	2010-10-18		Cumulative update package 12 (CU12) for SQL Server 2005 Service Pack 3\n" +
        "B	9.0.4309	SP3 CU11	CumulativeUpdate	2258854	2010-08-16		Cumulative update package 11 (CU11) for SQL Server 2005 Service Pack 3\n" +
        "B	9.0.4305	SP3 CU10	CumulativeUpdate	983329	2010-06-23		Cumulative update package 10 (CU10) for SQL Server 2005 Service Pack 3\n" +
        "B	9.0.4294	SP3 CU9	CumulativeUpdate	980176	2010-04-19		Cumulative update package 9 (CU9) for SQL Server 2005 Service Pack 3\n" +
        "B	9.0.4285	SP3 CU8	CumulativeUpdate	978915	2010-02-16		Cumulative update package 8 (CU8) for SQL Server 2005 Service Pack 3\n" +
        "B	9.0.4273	SP3 CU7	CumulativeUpdate	976951	2009-12-21		Cumulative update package 7 (CU7) for SQL Server 2005 Service Pack 3\n" +
        "B	9.0.4268	Hotfix	Hotfix	977151	2009-12-21		FIX: Error message when you add a subscription to a republisher that is in a merge publication in SQL Server 2005: \"Cannot create the subscription because the subscription already exists in the subscription database\"\n" +
        "B	9.0.4266	SP3 CU6	CumulativeUpdate	974648	2009-10-19		Cumulative update package 6 (CU6) for SQL Server 2005 Service Pack 3\n" +
        "B	9.0.4262	SP3	SecurityUpdate	970894	2009-10-13		MS09-062: Description of the security update for SQL Server 2005 Service Pack 3 QFE: October 13, 2009\n" +
        "B	9.0.4230	SP3 CU5	CumulativeUpdate	972511	2009-08-17		Cumulative update package 5 (CU5) for SQL Server 2005 Service Pack 3\n" +
        "B	9.0.4226	SP3 CU4	CumulativeUpdate	970279	2009-06-16		Cumulative update package 4 (CU4) for SQL Server 2005 Service Pack 3\n" +
        "B	9.0.4224	Hotfix	Hotfix	971409	2009-06-16		FIX: Error message when you run a query that contains duplicate join conditions in SQL Server 2005: \"Internal Query Processor Error: The query processor could not produce a query plan\"\n" +
        "B	9.0.4220	SP3 CU3	CumulativeUpdate	967909	2009-04-20		Cumulative update package 3 (CU3) for SQL Server 2005 Service Pack 3\n" +
        "B	9.0.4216	Hotfix	Hotfix	967101	2009-04-20		FIX: The performance of database mirroring decreases when you run a database maintenance job that generates a large number of transaction log activities in SQL Server 2005\n" +
        "B	9.0.4211	SP3 CU2	CumulativeUpdate	961930	2009-02-17		Cumulative update package 2 (CU2) for SQL Server 2005 Service Pack 3\n" +
        "B	9.0.4207	SP3 CU1	CumulativeUpdate	959195	2008-12-20		Cumulative update package 1 (CU1) for SQL Server 2005 Service Pack 3\n" +
        "B	9.0.4060	RTM + security update	SecurityUpdate	2494113	2011-06-14		MS11-049: Description of the security update for SQL Server 2005 Service Pack 3 GDR: June 14, 2011\n" +
        "B	9.0.4053	RTM + security update	SecurityUpdate	970892	2009-10-13		MS09-062: Description of the security update for SQL Server 2005 Service Pack 3 GDR: October 13, 2009\n" +
        "B	9.0.4035	SP3	ServicePack	955706	2008-12-15		SQL Server 2005 Service Pack 3 (SP3)\n" +
        "B	9.0.4028	CTP	Preview		2008-10-27		SQL Server 2005 Service Pack 3 (SP3) - CTP Beta\n" +
        "B	9.0.3356	SP2 CU17	CumulativeUpdate	976952	2009-12-21		Cumulative update package 17 (CU17) for SQL Server 2005 Service Pack 2\n" +
        "B	9.0.3355	SP2 CU16	CumulativeUpdate	974647	2009-10-19		Cumulative update package 16 (CU16) for SQL Server 2005 Service Pack 2\n" +
        "B	9.0.3353	SP2	SecurityUpdate	970896	2009-10-13		MS09-062: Description of the security update for SQL Server 2005 Service Pack 2 QFE: October 13, 2009\n" +
        "B	9.0.3330	SP2 CU15	CumulativeUpdate	972510	2009-08-18		Cumulative update package 15 (CU15) for SQL Server 2005 Service Pack 2\n" +
        "B	9.0.3328	SP2 CU14	CumulativeUpdate	970278	2009-06-16		Cumulative update package 14 (CU14) for SQL Server 2005 Service Pack 2\n" +
        "B	9.0.3325	SP2 CU13	CumulativeUpdate	967908	2009-04-20		Cumulative update package 13 (CU13) for SQL Server 2005 Service Pack 2\n" +
        "B	9.0.3320	Hotfix	Hotfix	969142	2009-04-01		FIX: Error message when you run the DBCC CHECKDB statement on a database in SQL Server 2005: \"Unable to deallocate a kept page\"\n" +
        "B	9.0.3318	Hotfix	Hotfix	967199	2009-04-20		FIX: The Wmiprvse.exe host process stops responding when you run a SQL Server 2005-based application that sends a Windows Management Instrumentation (WMI) query to the SQL Server WMI provider\n" +
        "B	9.0.3315	SP2 CU12	CumulativeUpdate	962970	2009-02-17		Cumulative update package 12 (CU12) for SQL Server 2005 Service Pack 2\n" +
        "B	9.0.3310		SecurityUpdate	960090	2009-02-10		MS09-004: Description of the security update for SQL Server 2005 QFE: February 10, 2009\n" +
        "B	9.0.3301	SP2 CU11	CumulativeUpdate	958735	2008-12-16		Cumulative update package 11 (CU11) for SQL Server 2005 Service Pack 2\n" +
        "B	9.0.3294	SP2 CU10	CumulativeUpdate	956854	2008-10-20		Cumulative update package 10 (CU10) for SQL Server 2005 Service Pack 2\n" +
        "B	9.0.3282	SP2 CU9	CumulativeUpdate	953752	2008-06-16		Cumulative update package 9 (CU9) for SQL Server 2005 Service Pack 2\n" +
        "B	9.0.3260	Hotfix	Hotfix	954950	2008-07-14		FIX: Error message when you run a distributed query in SQL Server 2005: \"OLE DB provider 'SQLNCLI' for linked server '<Linked Server>' returned message 'No transaction is active'\"\n" +
        "B	9.0.3259	Hotfix	Hotfix	954831	2008-08-14		FIX: In SQL Server 2005, the session that runs the TRUNCATE TABLE statement may stop responding, and you cannot end the session\n" +
        "B	9.0.3259	Hotfix	Hotfix	954669	2008-07-14		FIX: An ongoing MS DTC transaction is orphaned in SQL Server 2005\n" +
        "B	9.0.3257	SP2 CU8	CumulativeUpdate	951217	2008-06-18		Cumulative update package 8 (CU8) for SQL Server 2005 Service Pack 2\n" +
        "B	9.0.3246	Hotfix	Hotfix	952233	2008-05-23		FIX: All the MDX queries that are running on an instance of SQL Server 2005 Analysis Services are canceled when you start or stop a SQL Server Profiler trace for the instance\n" +
        "B	9.0.3244	Hotfix	Hotfix	952330	2008-06-03		FIX: The Replication Log Reader Agent may fail intermittently when a transactional replication synchronizes data in SQL Server 2005\n" +
        "B	9.0.3240	Hotfix	Hotfix	951204	2008-05-21		FIX: An access violation occurs when you update a table through a view by using a cursor in SQL Server 2005\n" +
        "B	9.0.3239	SP2 CU7	CumulativeUpdate	949095	2008-04-17		Cumulative update package 7 (CU7) for SQL Server 2005 Service Pack 2\n" +
        "B	9.0.3232	Hotfix	Hotfix	949959	2008-03-19		FIX: Error message when you synchronize the data of a merge replication in SQL Server 2005: \"The merge process is retrying a failed operation made to article 'ArticleName' - Reason: 'Invalid input parameter values. Check the status values for detail.'\"\n" +
        "B	9.0.3231	Hotfix	Hotfix	949595	2008-03-18		FIX: Error message when you run a query that uses a join condition in SQL Server 2005: \"Non-yielding Scheduler\"\n" +
        "B	9.0.3231	Hotfix	Hotfix	949687	2008-03-14		FIX: Error message when you run a transaction from a remote server by using a linked server in SQL Server 2005: \"This operation conflicts with another pending operation on this transaction\"\n" +
        "B	9.0.3230	Hotfix	Hotfix	949199	2008-03-07		FIX: Error message when you run queries on a database that has the SNAPSHOT isolation level enabled in SQL Server 2005: \"Unable to deallocate a kept page\"\n" +
        "B	9.0.3228	SP2 CU6	CumulativeUpdate	946608	2008-02-19		Cumulative update package 6 (CU6) for SQL Server 2005 Service Pack 2\n" +
        "B	9.0.3224	Hotfix	Hotfix	947463	2008-02-04		FIX: A stored procedure cannot finish its execution in SQL Server 2005\n" +
        "B	9.0.3221	Hotfix	Hotfix	942908	2008-01-31		FIX: The change may be undone during the later synchronizations when you change an article on the subscriber in SQL Server 2005\n" +
        "B	9.0.3221	Hotfix	Hotfix	945443	2008-01-11		FIX: A query takes longer to finish in SQL Server 2005 than in SQL Server 2000 when you open a fast forward-only cursor for the query\n" +
        "B	9.0.3221	Hotfix	Hotfix	945916	2008-01-10		FIX: Error messages when you delete some records of a table in a transaction or when you update some records of a table in a transaction in SQL Server 2005: \"Msg 9002,\" \"Msg 3314,\" and \"Msg 9001\"\n" +
        "B	9.0.3221	Hotfix	Hotfix	945442	2008-01-09		FIX: You cannot cancel the query execution immediately if you open a fast forward-only cursor for the query in SQL Server 2005\n" +
        "B	9.0.3215	SP2 CU5	CumulativeUpdate	943656	2007-12-18		Cumulative update package 5 (CU5) for SQL Server 2005 Service Pack 2\n" +
        "B	9.0.3208	Hotfix	Hotfix	944902	2007-11-21		FIX: A federated database server stops responding when you run parallel queries on a multiprocessor computer that uses NUMA architecture in SQL Server 2005\n" +
        "B	9.0.3206	Hotfix	Hotfix	944677	2007-12-11		FIX: Conflicts are not logged when you use the Microsoft SQL Server Subscriber Always Wins Conflict Resolver for an article in a merge replication in Microsoft SQL Server 2005\n" +
        "B	9.0.3200	SP2 CU4	CumulativeUpdate	941450	2007-10-17		Cumulative update package 4 (CU4) for SQL Server 2005 Service Pack 2\n" +
        "B	9.0.3194	Hotfix	Hotfix	940933	2007-09-24		FIX: Some changes from subscribers who use SQL Server 2005 Compact Edition or Web synchronization are not uploaded to the publisher when you use the republishing model in a merge publication in Microsoft SQL Server 2005\n" +
        "B	9.0.3186	Hotfix	Hotfix	940943	2007-08-29		FIX: The performance of a query that performs an insert operation or an update operation is much slower in SQL Server 2005 SP2 than in earlier versions of SQL Server 2005\n" +
        "B	9.0.3186	Hotfix	Hotfix	940378	2007-08-24		FIX: A cursor uses the incorrect transaction isolation level after you change the transaction isolation level for the cursor in SQL Server 2005\n" +
        "B	9.0.3186	Hotfix	Hotfix	940269	2007-08-23		FIX: Error message when you try to edit a SQL Server Agent job or a maintenance plan by using SQL Server Management Studio in SQL Server 2005: \"String or binary data would be truncated\"\n" +
        "B	9.0.3186	Hotfix	Hotfix	940945	2007-08-22		FIX: Performance is very slow when the same stored procedure is executed at the same time in many connections on a multiple-processor computer that is running SQL Server 2005\n" +
        "B	9.0.3186	Hotfix	Hotfix	940937	2007-08-21		FIX: Error message when you try to update the index key columns of a non-unique clustered index in SQL Server 2005: \"Cannot insert duplicate key row in object 'ObjectName' with unique index 'IndexName'\"\n" +
        "B	9.0.3186	Hotfix	Hotfix	940379	2007-08-20		FIX: Error message when you use the UNLOAD and REWIND options to back up a database to a tape device in SQL Server 2005: \"Operation on device '<TapeDevice>' exceeded retry count\"\n" +
        "B	9.0.3186	Hotfix	Hotfix	940375	2007-08-20		FIX: Error message when you use the Copy Database Wizard to move a database from SQL Server 2000 to SQL Server 2005\n" +
        "B	9.0.3186	Hotfix	Hotfix	937100	2007-08-20		FIX: Error message when you run a SQL Server 2005 Integration Services package that contains a Script Component transformation:\"Insufficient memory to continue the execution of the program\"\n" +
        "B	9.0.3186	Hotfix	Hotfix	940126	2007-08-20		FIX: Error 9003 is logged in the SQL Server error log file when you use log shipping in SQL Server 2005\n" +
        "B	9.0.3186	Hotfix	Hotfix	938363	2007-08-17		FIX: Data is not replicated to a subscriber in a different partition by using parameterized row filters in SQL Server 2005\n" +
        "B	9.0.3186	Hotfix	Hotfix	940935	2007-08-17		FIX: Error message when you run a query that is associated with a parallel execution plan in SQL Server 2005: \"SQL Server Assertion: File: <lckmgr.cpp>, line=10850 Failed Assertion = 'GetLocalLockPartition () == xactLockInfo->GetLocalLockPartition ()'\"\n" +
        "B	9.0.3186	SP2	Hotfix	940221	2007-08-17		FIX: Error message when you try to create an Oracle publication by using the New Publication Wizard in SQL Server 2005 Service Pack 2: \"OLE DB Provider 'OraOLEDB.ORACLE' for Linked server <LinkedServerName> returned message\"\n" +
        "B	9.0.3186	Hotfix	Hotfix	940942	2007-08-17		FIX: Error message when you run a stored procedure that references tables after you upgrade a database from SQL Server 2000 to SQL Server 2005: \"A time-out occurred while waiting for buffer latch\"\n" +
        "B	9.0.3186	SP2	Hotfix	940384	2007-08-13		FIX: You receive a System.InvalidCastException exception when you run an application that calls the Server.JobServer.Jobs.Contains method on a computer that has SQL Server 2005 Service Pack 2 installed\n" +
        "B	9.0.3186	Hotfix	Hotfix	940281	2007-08-13		FIX: An access violation may occur, and you may receive an error message, when you query the sys.dm_exe_sessions dynamic management view in SQL Server 2005\n" +
        "B	9.0.3186	Hotfix	Hotfix	940545	2007-08-10		FIX: The performance of insert operations against a table that contains an identity column may be slow in SQL Server 2005\n" +
        "B	9.0.3186	Hotfix	Hotfix	940210	2007-08-08		FIX: Error message when you try to insert more than 3 megabytes of data into a distributed partitioned view in SQL Server 2005: \"A system assertion check has failed\"\n" +
        "B	9.0.3186	SP2 CU3	CumulativeUpdate	939537	2007-08-23		Cumulative update package 3 (CU3) for SQL Server 2005 Service Pack 2\n" +
        "B	9.0.3182	Hotfix	Hotfix	940128	2007-08-03		FIX: You receive error 8623 when you run a complex query in SQL Server 2005\n" +
        "B	9.0.3179	Hotfix	Hotfix	938243	2007-07-30		FIX: Error message when you run a full-text query against a catalog in SQL Server 2005: \"The execution of a full-text query failed. The content index is corrupt.\"\n" +
        "B	9.0.3178	Hotfix	Hotfix	938086	2007-08-22		FIX: A SQL Server Agent job fails when you run the SQL Server Agent job in the context of a proxy account in SQL Server 2005\n" +
        "B	9.0.3177	Hotfix	Hotfix	939285	2007-08-22		FIX: Error message when you run a stored procedure that starts a transaction that contains a Transact-SQL statement in SQL Server 2005: \"New request is not allowed to start because it should come with valid transaction descriptor\"\n" +
        "B	9.0.3177	SP2	Hotfix	939562	2007-08-20		FIX: Error message when you run a query that fires an INSTEAD OF trigger in SQL Server 2005 Service Pack 2: \"Internal Query Processor Error The query processor could not produce a query plan\"\n" +
        "B	9.0.3177	Hotfix	Hotfix	939563	2007-08-09		FIX: Error message when you synchronize a merge replication in Microsoft SQL Server 2005: \"MSmerge_del_<GUID>, Line 42 String or binary data would be truncated\"\n" +
        "B	9.0.3175	Hotfix	Hotfix	936534	2007-08-20		FIX: Error message when the Distribution Agent tries to apply the snapshot to the subscriber in SQL Server 2005: \"Must declare the scalar variable \"@Variable\"\"\n" +
        "B	9.0.3175	Hotfix	Hotfix	938671	2007-08-01		FIX: The Distribution Agent may skip some rows when you configure a transactional replication that uses the \"-SkipErrors\" parameter in SQL Server 2005\n" +
        "B	9.0.3175	SP2	Hotfix	936488	2007-07-10		The service pack update or hotfix installation stops unexpectedly when you try to install either Microsoft SQL Server 2005 Service Pack 2 or a hotfix for SQL Server 2005 SP2\n" +
        "B	9.0.3175	Hotfix	Hotfix	938825	2007-06-29		FIX: A foreign key constraint that you drop on a table at the publisher is not dropped on the table at the subscriber in a SQL Server 2005 merge replication\n" +
        "B	9.0.3175	CU2	CumulativeUpdate	936305	2007-06-28		Cumulative update package 2 (CU2 build 3175) for SQL Server 2005 Service Pack 2 is available\n" +
        "B	9.0.3171	Hotfix	Hotfix	937745	2007-07-16		FIX: You may receive error messages when you try to log in to an instance of SQL Server 2005 and SQL Server handles many concurrent connections\n" +
        "B	9.0.3169	Hotfix	Hotfix	937033	2007-06-19		FIX: Error message when you run a linked server query in SQL Server 2005: \"The oledbprovider unisys.dmsII.1 for linkserver '<ServerName>' reported an error the provider ran out of memory\"\n" +
        "B	9.0.3169	Hotfix	Hotfix	937041	2007-05-25		FIX: Changes in the publisher database are not replicated to the subscribers in a transactional replication if the publisher database runs exposed in a database mirroring session in SQL Server 2005\n" +
        "B	9.0.3166	Hotfix	Hotfix	936185	2007-06-11		FIX: Blocking and performance problems may occur when you enable trace flag 1118 in SQL Server 2005 if the temporary table creation workload is high\n" +
        "B	9.0.3166	Hotfix	Hotfix	934734	2007-07-16		FIX: A database is marked as suspect when you update a table that contains a nonclustered index in SQL Server 2005\n" +
        "B	9.0.3161	Hotfix	Hotfix	935789	2007-09-24		FIX: On a computer that is running SQL Server 2005 and that has multiple processors, you may receive incorrect results when you run a query that contains an inner join\n" +
        "B	9.0.3161	SP2	Hotfix	934706	2007-06-04		FIX: Error message when you perform a piecemeal restore operation after you enable vardecimal database compression in SQL Server 2005 Service Pack 2: \"Piecemeal restore is not supported when an upgrade is involved\"\n" +
        "B	9.0.3161	Hotfix	Hotfix	933724	2007-05-09		FIX: The query performance is slow when you run a query that uses a user-defined scalar function against an instance of SQL Server 2005\n" +
        "B	9.0.3161	SP2	CumulativeUpdate	935356	2007-04-16		Cumulative update package (CU1 build 3161) for SQL Server 2005 Service Pack 2 is available\n" +
        "B	9.0.3159	Hotfix	Hotfix	934459	2007-04-03		FIX: The Check Database Integrity task and the Execute T-SQL Statement task in a maintenance plan may lose database context in certain circumstances in SQL Server 2005 builds 3150 through 3158\n" +
        "B	9.0.3156	Hotfix	Hotfix	934226	2007-04-25		FIX: Error message when you try to use Database Mail to send an e-mail message in SQL Server 2005: \"profile name is not valid (Microsoft SQL Server, Error 14607)\"\n" +
        "B	9.0.3155	Hotfix	Hotfix	933808	2007-06-13		FIX: Error message when you run a query that contains nested FOR XML clauses in SQL Server 2005: \"The XML data type is damaged\"\n" +
        "B	9.0.3155	Hotfix	Hotfix	933499	2007-06-12		FIX: Error message when you use transactional replication to replicate the execution of stored procedures to subscribers in SQL Server 2005: \"Insufficient memory to run query\"\n" +
        "B	9.0.3155	Hotfix	Hotfix	933766	2007-05-15		FIX: Failed assertion message in the Errorlog file when you perform various operations in SQL Server 2005: \"Failed Assertion = 'fFalse' Attempt to access expired blob handle (3)\"\n" +
        "B	9.0.3155	Hotfix	Hotfix	933549	2007-04-25		FIX: You may receive an access violation when you perform a bulk copy operation in SQL Server 2005\n" +
        "B	9.0.3154	Hotfix	Hotfix	934188	2007-04-25		FIX: The Distribution Agent does not deliver commands to the Subscriber even if the Distribution Agent is running in SQL Server 2005\n" +
        "B	9.0.3154	Hotfix	Hotfix	934109	2007-04-25		FIX: The Distribution Agent generates an access violation when you configure a transactional replication publication to run an additional script after the snapshot is applied at the subscriber in SQL Server 2005\n" +
        "B	9.0.3154	Hotfix	Hotfix	934106	2007-04-25		FIX: SQL Server 2005 database engine generates failed assertion errors when you use the Replication Monitor to monitor the distribution database\n" +
        "B	9.0.3153	Hotfix	Hotfix	933564	2007-04-16		FIX: A gradual increase in memory consumption for the USERSTORE_TOKENPERM cache store occurs in SQL Server 2005\n" +
        "B	9.0.3152	SP2	Hotfix	933097	2007-03-07		Cumulative hotfix package (build 3152) for SQL Server 2005 Service Pack 2 is available\n" +
        "B	9.0.3080	RTM + security update	SecurityUpdate	970895	2009-10-13		MS09-062: Description of the security update for GDI+ for SQL Server 2005 Service Pack 2 GDR: October 13, 2009\n" +
        "B	9.0.3077	RTM + security update	SecurityUpdate	960089	2009-02-10		MS09-004: Description of the security update for SQL Server 2005 GDR: February 10, 2009\n" +
        "B	9.0.3073	RTM + security update	SecurityUpdate	954606	2008-09-09		MS08-052: Description of the security update for GDI+ for SQL Server 2005 Service Pack 2 GDR: September 9, 2008\n" +
        "B	9.0.3068		Unknown	941203	2008-08-05		MS08-040: Vulnerabilities in Microsoft SQL Server could allow elevation of privilege\n" +
        "B	9.0.3054	Hotfix	Hotfix	934458	2008-01-02		FIX: The Check Database Integrity task and the Execute T-SQL Statement task in a maintenance plan may lose database context in certain circumstances in SQL Server 2005 builds 3042 through 3053\n" +
        "B	9.0.3050	SP2	ServicePack	933508	2007-03-07		Microsoft SQL Server 2005 Service Pack 2 issue: Cleanup tasks run at different intervals than intended\n" +
        "B	9.0.3042	SP2	ServicePack		2007-02-19		SQL Server 2005 Service Pack 2 (SP2)\n" +
        "B	9.0.3033	CTP	Preview		2006-12-19		SQL Server 2005 Service Pack 2 (SP2) - CTP December 2006 Beta\n" +
        "B	9.0.3027	CTP	Preview		2006-11-06		SQL Server 2005 Service Pack 2 (SP2) - CTP November 2006 Beta\n" +
        "B	9.0.3026	Hotfix	Hotfix	929376	2007-02-14		FIX: A \"17187\" error message may be logged in the Errorlog file when an instance of SQL Server 2005 is under a heavy load\n" +
        "B	9.0.2239	Hotfix	Hotfix	940961	2007-09-24		FIX: Transactions that are being committed on the principal server may not be copied to the mirror server when a database mirroring failover occurs in SQL Server 2005\n" +
        "B	9.0.2237	Hotfix	Hotfix	940719	2007-09-24		FIX: A memory leak occurs when you call the Initialize method and the Terminate method of the SQLDistribution object in a loop in an application that you develop by using Microsoft ActiveX replication controls in SQL Server 2005\n" +
        "B	9.0.2236	Hotfix	Hotfix	940287	2007-07-29		FIX: Error message when you use Service Broker in SQL Server 2005: \"An error occurred while receiving data: '64(The specified network name is no longer available.)'\"\n" +
        "B	9.0.2236	Hotfix	Hotfix	940286	2007-07-26		FIX: A Service Broker endpoint stops passing messages in a database mirroring session of SQL Server 2005\n" +
        "B	9.0.2234	Hotfix	Hotfix	937343	2007-06-20		FIX: SQL Server 2005 stops and then restarts unexpectedly and errors occur in the tempdb database\n" +
        "B	9.0.2233	Hotfix	Hotfix	937545	2007-06-18		FIX: Error message when you use the BULK INSERT statement to import a data file into a table in SQL Server 2005 with SP1: \"The OLE DB provider \"BULK\" for linked server \"(null)\" reported an error\"\n" +
        "B	9.0.2233	Hotfix	Hotfix	933499	2007-06-12		FIX: Error message when you use transactional replication to replicate the execution of stored procedures to subscribers in SQL Server 2005: \"Insufficient memory to run query\"\n" +
        "B	9.0.2233	Hotfix	Hotfix	937544	2007-06-05		FIX: You may receive error 3456 when you try to restore a transaction log for a SQL Server 2005 database\n" +
        "B	9.0.2232	Hotfix	Hotfix	937277	2007-06-19		FIX: A memory leak occurs when you use the sp_OAMethod stored procedure to call a method of a COM object in SQL Server 2005\n" +
        "B	9.0.2231	Hotfix	Hotfix	934812	2007-11-06		FIX: You cannot bring the SQL Server group online in a cluster environment after you rename the virtual server name of the default instance of SQL Server 2005\n" +
        "B	9.0.2230	Hotfix	Hotfix	936179	2007-09-20		FIX: Error message when you use SQL Native Client to connect to an instance of a principal server in a database mirroring session: \"The connection attempted to fail over to a server that does not have a failover partner\"\n" +
        "B	9.0.2229	Hotfix	Hotfix	935446	2007-06-11		FIX: You receive error messages when you use the BULK INSERT statement in SQL Server 2005 to import data in bulk\n" +
        "B	9.0.2227	Hotfix	Hotfix	933265	2007-06-26		FIX: You may receive error 1203 when you run an INSERT statement against a table that has an identity column in SQL Server 2005\n" +
        "B	9.0.2226	Hotfix	Hotfix	934065	2007-06-22		FIX: Error message when the Replication Merge Agent runs to synchronize a merge replication subscription in SQL Server 2005: \"The merge process failed to execute a query because the query timed out\"\n" +
        "B	9.0.2226	Hotfix	Hotfix	933762	2007-06-22		FIX: You receive error 18815 when the Log Reader Agent runs for a transactional publication in SQL Server 2005\n" +
        "B	9.0.2223	SP1	Hotfix	932393	2007-06-18		FIX: You may experience poor performance after you install SQL Server 2005 Service Pack 1\n" +
        "B	9.0.2221	Hotfix	Hotfix	931593	2007-07-11		FIX: A script task or a script component may not run correctly when you run an SSIS package in SQL Server 2005 build 2153 and later builds\n" +
        "B	9.0.2219	Hotfix	Hotfix	932115	2007-04-25		FIX: The ghost row clean-up thread does not remove ghost rows on some data files of a database in SQL Server 2005\n" +
        "B	9.0.2218	Hotfix	Hotfix	931843	2007-04-25		FIX: SQL Server 2005 does not reclaim the disk space that is allocated to the temporary table if the stored procedure is stopped\n" +
        "B	9.0.2216	Hotfix	Hotfix	931821	2007-05-15		FIX: High CPU utilization by SQL Server 2005 may occur when you use NUMA architecture on a computer that has an x64-based version of SQL Server 2005 installed\n" +
        "B	9.0.2214	Hotfix	Hotfix	930505	2007-02-19		FIX: Error message when you run DML statements against a table that is published for merge replication in SQL Server 2005: \"Could not find stored procedure\"\n" +
        "B	9.0.2214	Hotfix	Hotfix	929240	2007-02-13		FIX: I/O requests that are generated by the checkpoint process may cause I/O bottlenecks if the I/O subsystem is not fast enough to sustain the IO requests in SQL Server 2005\n" +
        "B	9.0.2211	Hotfix	Hotfix	930284	2007-02-20		FIX: You receive error 1456 when you try to add a witness to a DBM session in SQL Server 2005\n" +
        "B	9.0.2211	Hotfix	Hotfix	930283	2007-02-14		FIX: You receive error 1456 when you add a witness to a database mirroring session and the database name is the same as an existing database mirroring session in SQL Server 2005\n" +
        "B	9.0.2209	Hotfix	Hotfix	929278	2007-02-07		FIX: SQL Server 2005 may not perform histogram amendments when you use trace flags 2389 and 2390\n" +
        "B	9.0.2208	Hotfix	Hotfix	929179	2007-01-09		FIX: A memory leak may occur every time that you synchronize a SQL Server Mobile subscriber in SQL Server 2005\n" +
        "B	9.0.2207	Hotfix	Hotfix	928394	2006-12-19		FIX: The changes are not reflected in the publication database after you reinitialize the subscriptions in SQL Server 2005\n" +
        "B	9.0.2207	Hotfix	Hotfix	928372	2006-12-19		FIX: Error message when you use a synonym for a stored procedure in SQL Server 2005: \"A severe error occurred on the current command\"\n" +
        "B	9.0.2207	Hotfix	Hotfix	928789	2007-01-02		FIX: Error message in the database mail log when you try to use the sp_send_dbmail stored procedure to send an e-mail in SQL Server 2005: \"Invalid XML message format received on the ExternalMailQueue\"\n" +
        "B	9.0.2206	Hotfix	Hotfix	928083	2007-02-01		FIX: You may receive an error message when you run a CLR stored procedure or CLR function that uses a context connection in SQL Server 2005\n" +
        "B	9.0.2206	Hotfix	Hotfix	928537	2007-01-12		FIX: The full-text index population for the indexed view is very slow in SQL Server 2005\n" +
        "B	9.0.2206	Hotfix	Hotfix	926493	2007-01-02		FIX: Error message when you restore a transaction-log backup that is generated in SQL Server 2000 SP4 to an instance of SQL Server 2005: Msg 3456, Level 16, State 1, Line 1. Could not redo log record\"\n" +
        "B	9.0.2206	Hotfix	Hotfix	928539	2006-12-13		FIX: An access violation is logged in the SQL Server Errorlog file when you run a query that uses a plan guide in SQL Server 2005\n" +
        "B	9.0.2202	Hotfix	Hotfix	927643	2007-02-16		FIX: Some search results are missing when you perform a full-text search operation on a Windows SharePoint Services 2.0 site after you upgrade to SQL Server 2005\n" +
        "B	9.0.2201	Hotfix	Hotfix	927289	2007-01-10		FIX: Updates to the SQL Server Mobile subscriber may not be reflected in the SQL Server 2005 merge publication\n" +
        "B	9.0.2198	Hotfix	Hotfix	926613	2007-02-21		FIX: You may receive incorrect results when you query a table that is published in a transactional replication in SQL Server 2005\n" +
        "B	9.0.2198	Hotfix	Hotfix	926106	2007-02-20		FIX: You receive an error message when you use the Print Preview option on a large report in SQL Server 2005 Reporting Services\n" +
        "B	9.0.2198	Hotfix	Hotfix	924807	2007-02-02		FIX: The restore operation may take a long time to finish when you restore a database in SQL Server 2005\n" +
        "B	9.0.2198	Hotfix	Hotfix	924264	2006-12-13		FIX: The metadata of the Description object of a Key Performance Indicator appears in the default language after you define a translation for the Description object in SQL Server 2005 Business Intelligence Development Studio\n" +
        "B	9.0.2198	Hotfix	Hotfix	926612	2007-01-04		FIX: SQL Server Agent does not send an alert quickly or does not send an alert when you use an alert of the SQL Server event alert type in SQL Server 2005\n" +
        "B	9.0.2198	Hotfix	Hotfix	926773	2006-11-16		FIX: Error message when you run a query that uses a fast forward-only cursor in SQL Server 2005: \"Query processor could not produce a query plan because of the hints defined in this query\"\n" +
        "B	9.0.2198	Hotfix	Hotfix	926611	2006-11-28		FIX: SQL Server 2005 may not send a message notification that is based on the specific string in the forwarded event when a computer that is running SQL Server 2000 forwards an event to a computer that is running SQL Server 2005\n" +
        "B	9.0.2198	Hotfix	Hotfix	924808	2006-12-13		FIX: You receive an error message, or you obtain an incorrect result when you query data in a partitioned table that does not have a clustered index in SQL Server 2005\n" +
        "B	9.0.2198	Hotfix	Hotfix	925277	2007-01-02		FIX: You may experience very large growth increments of a principal database after you manually fail over a database mirroring session in SQL Server 2005\n" +
        "B	9.0.2196		Unknown	926285	2006-11-10		Fix: Error message when you convert a column from the varbinary(max) data type to the XML data type in SQL Server 2005: \"Msg 6322, Level 16, State 1, Line 2 Too many attributes or namespace definitions\"\n" +
        "B	9.0.2196	Hotfix	Hotfix	926335	2006-12-05		FIX: Error message when you trace the Audit Database Management event and you try to bring a database online in SQL Server 2005: “Msg 942, Level 14, State 4, Line 1”\n" +
        "B	9.0.2195	Hotfix	Hotfix	926240	2006-12-19		FIX: SQL Server 2005 may stop responding when you use the SqlBulkCopy class to import data from another data source\n" +
        "B	9.0.2194	Hotfix	Hotfix	925744	2006-10-20		FIX: Error message when you try to use a SQL Server authenticated login to log on to an instance of SQL Server 2005: \"Logon error: 18456\"\n" +
        "B	9.0.2192	Hotfix	Hotfix	924954	2006-09-29		FIX: Error message when you use a table-valued function (TVF) together with the CROSS APPLY operator in a query in SQL Server 2005: \"There is insufficient system memory to run this query\"\n" +
        "B	9.0.2192	Hotfix	Hotfix	925335	2006-10-05		FIX: Error message when you use a label after a Transact-SQL query in SQL Server 2005: \"Incorrect syntax near 'X'\"\n" +
        "B	9.0.2191	Hotfix	Hotfix	925135	2006-12-06		FIX: An empty string is replicated as a NULL value when you synchronize a table to a SQL Server 2005 Compact Edition subscriber\n" +
        "B	9.0.2190	Hotfix	Hotfix	925227	2006-10-16		FIX: Error message when you call the SQLTables function against an instance of SQL Server 2005: \"Invalid cursor state (0)\"\n" +
        "B	9.0.2189	Hotfix	Hotfix	925153	2006-09-22		FIX: You may receive different date values for each row when you use the getdate function within a case statement in SQL Server 2005\n" +
        "B	9.0.2187	Hotfix	Hotfix	923849	2006-09-22		FIX: When you run a query that references a partitioned table in SQL Server 2005, query performance may decrease\n" +
        "B	9.0.2181	Hotfix	Hotfix	923605	2007-02-19		FIX: A deadlock occurs and a query never finishes when you run the query on a computer that is running SQL Server 2005 and has multiple processors\n" +
        "B	9.0.2181	Hotfix	Hotfix	923624	2006-10-04		FIX: Error message when you run an application against SQL Server 2005 that uses many unique user logins or performs many user login impersonations: \"insufficient system memory to run this query\"\n" +
        "B	9.0.2176	Hotfix	Hotfix	922594	2007-02-12		FIX: Error message when you use SQL Server 2005: \"High priority system task thread Operating system error Exception 0xAE encountered\"\n" +
        "B	9.0.2176	Hotfix	Hotfix	923296	2006-09-06		FIX: Log Reader Agent fails, and an assertion error message is logged when you use transactional replication in SQL Server 2005\n" +
        "B	9.0.2175	Hotfix	Hotfix	921395	2006-08-08		FIX: The color and the background image may not appear when you try to display a report in HTML format in Report Manager in SQL Server 2005 Reporting Services\n" +
        "B	9.0.2175	Hotfix	Hotfix	917905	2006-08-14		FIX: SQL Server 2005 performance may be slower than SQL Server 2000 performance when you use an API server cursor\n" +
        "B	9.0.2175	Hotfix	Hotfix	922578	2006-08-30		FIX: In SQL Server 2005, the sp_altermessage stored procedure does not suppress system error messages that are logged in the SQL Server error log and in the Application log\n" +
        "B	9.0.2175	Hotfix	Hotfix	922438	2006-12-14		FIX: A query may take a long time to compile when the query contains several JOIN clauses against a SQL Server 2005 database\n" +
        "B	9.0.2175	Hotfix	Hotfix	921536	2006-12-18		FIX: A handled access violation may occur in the CValSwitch::GetDataX function when you run a complex query in SQL Server 2005\n" +
        "B	9.0.2174	SP1	Hotfix	922063	2006-07-25		FIX: You may notice a large increase in compile time when you enable trace flags 2389 and 2390 in SQL Server 2005 Service Pack 1\n" +
        "B	9.0.2167	Hotfix	Hotfix	920974	2006-08-09		FIX: SQL Server 2005 treats an identity column in a view as an ordinary int column when the compatibility level of the database is set to 80\n" +
        "B	9.0.2164	Hotfix	Hotfix	919243	2007-02-08		FIX: Some rows in the Text Data column are always displayed for a trace that you create by using SQL Server Profiler in SQL Server 2005\n" +
        "B	9.0.2164	Hotfix	Hotfix	920346	2006-09-19		FIX: SQL Server 2005 may overestimate the cardinality of the JOIN operator when a SQL Server 2005 query contains a join predicate that is a multicolumn predicate\n" +
        "B	9.0.2164	Hotfix	Hotfix	920347	2006-09-19		FIX: The SQL Server 2005 query optimizer may incorrectly estimate the cardinality for a query that has a predicate that contains an index union alternative\n" +
        "B	9.0.2164	Hotfix	Hotfix	919929	2006-10-26		FIX: Error message when the Replication Merge Agent runs in SQL Server 2005: \"Source: MSSQL_REPL, Error number: MSSQL_REPL-2147199402\"\n" +
        "B	9.0.2164	Hotfix	Hotfix	921003	2006-08-29		FIX: You may receive an error message when you manually define a Back Up Database task in SQL Server 2005 to back up the transaction log\n" +
        "B	9.0.2164	Hotfix	Hotfix	920206	2006-09-26		FIX: System performance may be slow when an application submits many queries against a SQL Server 2005 database that uses simple parameterization\n" +
        "B	9.0.2164	Hotfix	Hotfix	918882	2006-09-06		FIX: A query plan is not cached in SQL Server 2005 when the text of the hint is a large object\n" +
        "B	9.0.2164	Hotfix	Hotfix	919636	2006-07-26		FIX: Memory usage of the compiled query plan may unexpectedly increase in SQL Server 2005\n" +
        "B	9.0.2164	Hotfix	Hotfix	919775	2006-08-09		FIX: The BULK INSERT statement may not return any errors when you try to import data from a text file to a table by using the BULK INSERT statement in Microsoft SQL Server 2005\n" +
        "B	9.0.2156	SP1	Hotfix	919611	2006-07-26		FIX: The value of the automatic growth increment of a database file may be very large in SQL Server 2005 with Service Pack 1\n" +
        "B	9.0.2153	Hotfix	Hotfix	918222	2006-09-14		Cumulative hotfix package (build 2153) for SQL Server 2005 is available\n" +
        "B	9.0.2153	Hotfix	Hotfix	919224	2006-05-23		FIX: You may receive an error message when you install the cumulative hotfix package (build 2153) for SQL Server 2005\n" +
        "B	9.0.2050	Hotfix	Hotfix	932555	2007-07-11		FIX: A script task or a script component may not run correctly when you run an SSIS package in SQL Server 2005 build 2047\n" +
        "B	9.0.2047	SP1	ServicePack		2006-04-18		SQL Server 2005 Service Pack 1 (SP1)\n" +
        "B	9.0.2040	CTP	Preview		2006-03-12		SQL Server 2005 Service Pack 1 (SP1) CTP March 2006 Beta\n" +
        "B	9.0.2029	SP1	Preview				SQL Server 2005 Service Pack 1 (SP1) Beta Beta\n" +
        "B	9.0.1561	Hotfix	Hotfix	932556	2007-07-11		FIX: A script task or a script component may not run correctly when you run an SSIS package in SQL Server 2005 build 1500 and later builds\n" +
        "B	9.0.1558	Hotfix	Hotfix	926493	2007-01-04		FIX: Error message when you restore a transaction-log backup that is generated in SQL Server 2000 SP4 to an instance of SQL Server 2005: \"Msg 3456, Level 16, State 1, Line 1. Could not redo log record\"\n" +
        "B	9.0.1554	Hotfix	Hotfix	926292	2007-06-26		FIX: When you query through a view that uses the ORDER BY clause in SQL Server 2005, the result is still returned in random order\n" +
        "B	9.0.1551	Hotfix	Hotfix	922527	2007-01-22		FIX: Error message when you schedule some SQL Server 2005 Integration Services packages to run as jobs: \"Package <PackageName> has been cancelled\"\n" +
        "B	9.0.1551	Hotfix	Hotfix	922804	2006-11-22		FIX: After you detach a Microsoft SQL Server 2005 database that resides on network-attached storage, you cannot reattach the SQL Server database\n" +
        "B	9.0.1550	Hotfix	Hotfix	917887	2006-07-26		FIX: The value of the automatic growth increment of a database file may be very large in SQL Server 2005\n" +
        "B	9.0.1550	Hotfix	Hotfix	921106	2006-11-22		FIX: You receive an error message when you try to create a differential database backup in SQL Server 2005\n" +
        "B	9.0.1547	Hotfix	Hotfix	918276	2006-11-20		FIX: You notice additional random trailing character in values when you retrieve the values from a fixed-size character column or a fixed-size binary column of a table in SQL Server 2005\n" +
        "B	9.0.1545	Hotfix	Hotfix	917905	2006-08-14		FIX: SQL Server 2005 performance may be slower than SQL Server 2000 performance when you use an API server cursor\n" +
        "B	9.0.1541	Hotfix	Hotfix	917888	2006-11-22		FIX: Error message when you use a server-side cursor to run a large complex query in SQL Server 2005: \"Error: 8623, Severity: 16, State: 1 The query processor ran out of internal resources\"\n" +
        "B	9.0.1541	Hotfix	Hotfix	917971	2006-11-22		FIX: You may receive more than 100,000 page faults when you try to back up a SQL Server 2005 database that contains hundreds of files and file groups\n" +
        "B	9.0.1539	Hotfix	Hotfix	917738	2006-08-11		FIX: SQL Server 2005 system performance may be slow when you use a keyset-driven cursor to execute a FETCH statement\n" +
        "B	9.0.1538	Hotfix	Hotfix	917824	2006-07-26		FIX: The SQL Server 2005 SqlCommandBuilder.DeriveParameters method returns an exception when the input parameter is a XML parameter that has an associated XSD from an SQL schema\n" +
        "B	9.0.1536	Hotfix	Hotfix	917016	2006-07-26		FIX: The monitor server does not monitor all primary servers and secondary servers when you configure log shipping in SQL Server 2005\n" +
        "B	9.0.1534	Hotfix	Hotfix	916706	2007-05-15		FIX: When you run the \"dbcc dbreindex\" command or the \"alter index\" command, some transactions are not replicated to the subscribers in a transactional replication in SQL Server 2005\n" +
        "B	9.0.1533	Hotfix	Hotfix	916086	2006-07-26		FIX: Errors may be generated in the tempdb database when you create and then drop many temporary tables in SQL Server 2005\n" +
        "B	9.0.1532	Hotfix	Hotfix	916046	2007-01-09		FIX: Indexes may grow very large when you insert a row into a table and then update the same row in SQL Server 2005\n" +
        "B	9.0.1531	Hotfix	Hotfix	915918	2006-07-26		FIX: The internal deadlock monitor may not detect a deadlock between two or more sessions in SQL Server 2005\n" +
        "B	9.0.1528	Hotfix	Hotfix	915309	2007-01-15		FIX: When you start a merge agent, synchronization between the subscriber and the publisher takes a long time to be completed in SQL Server 2005\n" +
        "B	9.0.1528	Hotfix	Hotfix	915308	2007-01-04		FIX: The CPU usage of the server reaches 100% when many DML activities occur in SQL Server 2005\n" +
        "B	9.0.1528	Hotfix	Hotfix	915307	2007-01-11		FIX: You experience a slow uploading process if conflicts occur when many merge agents upload changes to the publishers at the same time in SQL Server 2005\n" +
        "B	9.0.1528	Hotfix	Hotfix	915306	2007-01-08		FIX: The merge agent fails and a \"permission denied\" error message is logged when you synchronize a SQL Server 2005-based merge publication\n" +
        "B	9.0.1528	Hotfix	Hotfix	915112	2006-07-26		FIX: Error message when an ADO.NET-connected application tries to reuse a connection from the connection pool in SQL Server 2005: \"The request failed to run because the batch is aborted\"\n" +
        "B	9.0.1519	Hotfix	Hotfix	913494	2007-01-20		FIX: The merge agent does not use a specified custom user update to handle conflicting UPDATE statements in SQL Server 2005\n" +
        "B	9.0.1518	Hotfix	Hotfix	913941	2006-09-22		FIX: A SQL Server login may have more permissions when you log on to an instance of SQL Server 2005\n" +
        "B	9.0.1518	Hotfix	Hotfix	912472	2006-07-26		FIX: An incorrect result may appear in the subscribing database when you set database mirroring for a database and database failover occurs in SQL Server 2005\n" +
        "B	9.0.1518	Hotfix	Hotfix	913371	2006-07-26		FIX: You may receive error messages when you use the sp_cursoropen statement to open a cursor on a user-defined stored procedure in SQL Server 2005\n" +
        "B	9.0.1514	Hotfix	Hotfix	912471	2006-07-26		FIX: The replication on the server does not work any longer when you manually fail over databases in SQL Server 2005\n" +
        "B	9.0.1503	Hotfix	Hotfix	911662	2006-07-26		FIX: You may receive an access violation error message when you run a SELECT query in SQL Server 2005\n" +
        "B	9.0.1502	Hotfix	Hotfix	915793	2006-07-26		FIX: You cannot restore the log backups on the mirror server after you remove database mirroring for the mirror database in SQL Server 2005\n" +
        "B	9.0.1500	Hotfix	Hotfix	910416	2006-06-01		FIX: Error message when you run certain queries or certain stored procedures in SQL Server 2005: \"A severe error occurred on the current command\"\n" +
        "B	9.0.1406	Hotfix	Hotfix	932557	2007-07-11		FIX: A script task or a script component may not run correctly when you run an SSIS package in SQL Server 2005 build 1399\n" +
        "B	9.0.1399	RTM	Rtm		2005-11-07		SQL Server 2005 RTM\n" +
        "R	8.0	SQL Server 2000	Shiloh	8.0.194	2000-11-30	2008-04-08	2013-04-09\n" +
        "B	8.0.2305	SP4	SecurityUpdate	983811	2012-08-14		MS12-060: Description of the security update for SQL Server 2000 Service Pack 4 QFE: August 14, 2012\n" +
        "B	8.0.2301	SP4	SecurityUpdate	983809	2012-04-10		MS12-027: Description of the security update for Microsoft SQL Server 2000 Service Pack 4 QFE: April 10, 2012\n" +
        "B	8.0.2283	Hotfix	SecurityUpdate	971524	2009-06-15		FIX: An access violation occurs when you run a DELETE statement or an UPDATE statement in the Itanium-based versions of SQL Server 2000 after you install security update MS09-004\n" +
        "B	8.0.2282		SecurityUpdate	960083	2009-02-10		MS09-004: Description of the security update for SQL Server 2000 QFE and for MSDE 2000: February 10, 2009\n" +
        "B	8.0.2279	Hotfix	Hotfix	959678	2009-04-08		FIX: When you run the SPSBackup.exe utility to back up a SQL Server 2000 database that is configured as a back-end database for a Windows SharePoint Services server, the backup operation fails\n" +
        "B	8.0.2273		SecurityUpdate	948111	2008-08-05		MS08-040: Description of the security update for SQL Server 2000 QFE and MSDE 2000 July 8, 2008\n" +
        "B	8.0.2271	Hotfix	Hotfix	946584	2008-03-12		FIX: The SPACE function always returns one space in SQL Server 2000 if the SPACE function uses a collation that differs from the collation of the current database\n" +
        "B	8.0.2265	Hotfix	Hotfix	944985	2007-12-19		FIX: The data on the publisher does not match the data on the subscriber when you synchronize a SQL Server 2005 Mobile Edition subscriber with a SQL Server 2000 \"merge replication\" publisher\n" +
        "B	8.0.2253	Hotfix	Hotfix	939317	2007-10-09		FIX: The CPU utilization may suddenly increase to 100 percent when there are many connections to an instance of SQL Server 2000 on a computer that has multiple processors\n" +
        "B	8.0.2249	Hotfix	Hotfix	936232	2007-05-25		FIX: An access violation may occur when you try to log in to an instance of SQL Server 2000\n" +
        "B	8.0.2248	Hotfix	Hotfix	935950	2007-06-14		FIX: The foreign key that you created between two tables does not work after you run the CREATE INDEX statement in SQL Server 2000\n" +
        "B	8.0.2246		Unknown	935465	2007-06-18		An updated version of Sqlvdi.dll is now available for SQL Server 2000\n" +
        "B	8.0.2245	Hotfix	Hotfix	933573	2007-04-24		FIX: You may receive an assertion or database corruption may occur when you use the bcp utility or the \"Bulk Insert\" Transact-SQL command to import data in SQL Server 2000\n" +
        "B	8.0.2244	SP4	Hotfix	934203	2007-05-10		FIX: A hotfix for Microsoft SQL Server 2000 Service Pack 4 may not update all the necessary files on an x64-based computer\n" +
        "B	8.0.2242	Hotfix	Hotfix	929131	2007-03-28		FIX: In SQL Server 2000, the synchronization process is slow, and the CPU usage is high on the computer that is configured as the Distributor\n" +
        "B	8.0.2238	SP4	Hotfix	931932	2007-02-21		FIX: The merge agent fails intermittently when you use merge replication that uses a custom resolver after you install SQL Server 2000 Service Pack 4\n" +
        "B	8.0.2236	Hotfix	Hotfix	930484	2007-02-02		FIX: CPU utilization may approach 100 percent on a computer that is running SQL Server 2000 after you run the BACKUP DATABASE statement or the BACKUP LOG statement\n" +
        "B	8.0.2234	Hotfix	Hotfix	929440	2007-02-22		FIX: Error messages when you try to update table rows or insert table rows into a table in SQL Server 2000: \"644\" or \"2511\"\n" +
        "B	8.0.2232	Hotfix	Hotfix	928568	2007-01-15		FIX: SQL Server 2000 stops responding when you cancel a query or when a query time-out occurs, and error messages are logged in the SQL Server error log file\n" +
        "B	8.0.2231	Hotfix	Hotfix	928079	2007-06-19		FIX: The Sqldumper.exe utility cannot generate a filtered SQL Server dump file when you use the Remote Desktop Connection service or Terminal Services to connect to a Windows 2000 Server-based computer in SQL Server 2000\n" +
        "B	8.0.2229	SP4	Hotfix	927186	2007-07-24		FIX: Error message when you create a merge replication for tables that have computed columns in SQL Server 2000 Service Pack 4: \"The process could not log conflict information\"\n" +
        "B	8.0.2226	Hotfix	Hotfix	925684	2006-11-20		FIX: You may experience one or more symptoms when you run a \"CREATE INDEX\" statement on an instance of SQL Server 2000\n" +
        "B	8.0.2226	Hotfix	Hotfix	925732	2006-11-13		FIX: You may receive inconsistent comparison results when you compare strings by using a width sensitive collation in SQL Server 2000\n" +
        "B	8.0.2223	Hotfix	Hotfix	925419	2007-07-20		FIX: The server stops responding, the performance is slow, and a time-out occurs in SQL Server 2000\n" +
        "B	8.0.2223	SP4	Hotfix	925678	2006-10-31		FIX: Error message when you schedule a Replication Merge Agent job to run after you install SQL Server 2000 Service Pack 4: \"The process could not enumerate changes at the 'Subscriber'\"\n" +
        "B	8.0.2218	Hotfix	Hotfix	925297	2007-06-19		FIX: The result may be sorted in the wrong order when you run a query that uses the ORDER BY clause to sort a column in a table in SQL Server 2000\n" +
        "B	8.0.2217	Hotfix	Hotfix	924664	2007-10-25		FIX: You cannot stop the SQL Server service, or many minidump files and many log files are generated in SQL Server 2000\n" +
        "B	8.0.2215	Hotfix	Hotfix	923796	2007-01-12		FIX: Data in a subscriber of a merge publication in SQL Server 2000 differs from the data in the publisher\n" +
        "B	8.0.2215	Hotfix	Hotfix	924662	2006-10-05		FIX: The query performance may be slow when you query data from a view in SQL Server 2000\n" +
        "B	8.0.2215	Hotfix	Hotfix	923563	2006-10-30		FIX: Error message when you configure an immediate updating transactional replication in SQL Server 2000: \"Implicit conversion from datatype 'text' to 'nvarchar' is not allowed\"\n" +
        "B	8.0.2215	Hotfix	Hotfix	923327	2006-12-28		FIX: You may receive an access violation error message when you import data by using the \"Bulk Insert\" command in SQL Server 2000\n" +
        "B	8.0.2209		Unknown	923797			The Knowledge Base (KB) Article You Requested Is Currently Not Available\n" +
        "B	8.0.2207	Hotfix	Hotfix	923344	2006-08-28		FIX: A SQL Server 2000 session may be blocked for the whole time that a Snapshot Agent job runs\n" +
        "B	8.0.2201	Hotfix	Hotfix	920930	2006-08-21		FIX: Error message when you try to run a query on a linked server in SQL Server 2000\n" +
        "B	8.0.2199	Hotfix	Hotfix	919221	2006-07-26		FIX: SQL Server 2000 may take a long time to complete the synchronization phase when you create a merge publication\n" +
        "B	8.0.2197	Hotfix	Hotfix	919133	2006-08-02		FIX: Each query takes a long time to compile when you execute a single query or when you execute multiple concurrent queries in SQL Server 2000\n" +
        "B	8.0.2197	Hotfix	Hotfix	919068	2006-08-08		FIX: The query may return incorrect results, and the execution plan for the query may contain a \"Table Spool\" operator in SQL Server 2000\n" +
        "B	8.0.2197	Hotfix	Hotfix	919399	2006-10-18		FIX: A profiler trace in SQL Server 2000 may stop logging events unexpectedly, and you may receive the following error message: \"Failed to read trace data\"\n" +
        "B	8.0.2196	Hotfix	Hotfix	919165	2006-08-14		FIX: A memory leak occurs when you run a remote query by using a linked server in SQL Server 2000\n" +
        "B	8.0.2194	Hotfix	Hotfix	917565	2007-02-21		FIX: Error 17883 is logged in the SQL Server error log, and the instance of SQL Server 2000 temporarily stops responding\n" +
        "B	8.0.2194	Hotfix	Hotfix	917972	2006-09-22		FIX: You receive an access violation error message when you try to perform a read of a large binary large object column in SQL Server 2000\n" +
        "B	8.0.2192	SP4	Hotfix	917606	2006-08-04		FIX: You may notice a decrease in performance when you run a query that uses the UNION ALL operator in SQL Server 2000 Service Pack 4\n" +
        "B	8.0.2191	Hotfix	Hotfix	916698	2006-07-26		FIX: Error message when you run SQL Server 2000: \"Failed assertion = 'lockFound == TRUE'\"\n" +
        "B	8.0.2191	Hotfix	Hotfix	916950	2006-10-03		FIX: You may experience heap corruption, and SQL Server 2000 may shut down with fatal access violations when you try to browse files in SQL Server 2000 Enterprise Manager on a Windows Server 2003 x64-based computer\n" +
        "B	8.0.2189	Hotfix	Hotfix	916652	2006-07-26		FIX: An access violation may occur when you run a query on a table that has a multicolumn index in SQL Server 2000\n" +
        "B	8.0.2189	Hotfix	Hotfix	913438	2006-07-19		FIX: The SQL Server process may end unexpectedly when you turn on trace flag -T1204 and a profiler trace is capturing the Lock:DeadLock Chain event in SQL Server 2000 SP4\n" +
        "B	8.0.2187	Hotfix	Hotfix	915340	2007-06-18		FIX: A deadlock occurs when the scheduled SQL Server Agent job that you add or that you update is running in SQL Server 2000\n" +
        "B	8.0.2187	SP4	Hotfix	916287	2006-10-16		A cumulative hotfix package is available for SQL Server 2000 Service Pack 4 build 2187\n" +
        "B	8.0.2187	Hotfix	Hotfix	914384	2006-07-26		FIX: The database status changes to Suspect when you perform a bulk copy in a transaction and then roll back the transaction in SQL Server 2000\n" +
        "B	8.0.2187	Hotfix	Hotfix	915065	2006-12-11		FIX: Error message when you try to apply a hotfix on a SQL Server 2000-based computer that is configured as a MSCS node: \"An error in updating your system has occurred\"\n" +
        "B	8.0.2180	Hotfix	Hotfix	913789	2007-02-19		FIX: The password that you specify in a BACKUP statement appears in the SQL Server Errorlog file or in the Application event log if the BACKUP statement does not run in SQL Server 2000\n" +
        "B	8.0.2180	Hotfix	Hotfix	913684	2006-07-26		FIX: You may receive error messages when you use linked servers in SQL Server 2000 on a 64-bit Itanium processor\n" +
        "B	8.0.2175	Hotfix	Hotfix	911678	2006-07-26		FIX: No rows may be returned, and you may receive an error message when you try to import SQL Profiler trace files into tables by using the fn_trace_gettable function in SQL Server 2000\n" +
        "B	8.0.2172	Hotfix	Hotfix	910707	2006-07-26		FIX: When you query a view that was created by using the VIEW_METADATA option, an access violation may occur in SQL Server 2000\n" +
        "B	8.0.2171	Hotfix	Hotfix	909369	2006-07-26		FIX: Automatic checkpoints on some SQL Server 2000 databases do not run as expected\n" +
        "B	8.0.2168	Hotfix	Hotfix	907813	2006-11-21		FIX: An error occurs when you try to access the Analysis Services performance monitor counter object after you apply Windows Server 2003 SP1\n" +
        "B	8.0.2166	SP4	Hotfix	909734	2006-07-26		FIX: An error message is logged, and new diagnostics do not capture the thread stack when the SQL Server User Mode Scheduler (UMS) experiences a nonyielding thread in SQL Server 2000 Service Pack 4\n" +
        "B	8.0.2162	SP4	Hotfix	904660	2006-09-15		A cumulative hotfix package is available for SQL Server 2000 Service Pack 4 build 2162\n" +
        "B	8.0.2159	Hotfix	Hotfix	907250	2006-07-26		FIX: You may experience concurrency issues when you run the DBCC INDEXDEFRAG statement in SQL Server 2000\n" +
        "B	8.0.2156	Hotfix	Hotfix	906790	2006-07-25		FIX: You receive an error message when you try to rebuild the master database after you have installed hotfix builds in SQL Server 2000 SP4 64-bit\n" +
        "B	8.0.2151	Hotfix	Hotfix	903742	2006-07-25		FIX: You receive an \"Error: 8526, Severity: 16, State: 2\" error message in SQL Profiler when you use SQL Query Analyzer to start or to enlist into a distributed transaction after you have installed SQL Server 2000 SP4\n" +
        "B	8.0.2151	SP4	Hotfix	904244	2007-06-13		FIX: Incorrect data is inserted unexpectedly when you perform a bulk copy operation by using the DB-Library API in SQL Server 2000 Service Pack 4\n" +
        "B	8.0.2148	Hotfix	Hotfix	899430	2006-07-25		FIX: An access violation may occur when you run a SELECT query and the NO_BROWSETABLE option is set to ON in Microsoft SQL Server 2000\n" +
        "B	8.0.2148	SP4	Hotfix	899431	2006-07-25		FIX: An access violation occurs in the Mssdi98.dll file, and SQL Server crashes when you use SQL Query Analyzer to debug a stored procedure in SQL Server 2000 Service Pack 4\n" +
        "B	8.0.2148	Hotfix	Hotfix	900390	2006-06-01		FIX: The Mssdmn.exe process may use lots of CPU capacity when you perform a SQL Server 2000 full text search of Office Word documents\n" +
        "B	8.0.2148	Hotfix	Hotfix	900404	2006-06-01		FIX: The results of the query may be returned much slower than you expect when you run a query that includes a GROUP BY statement in SQL Server 2000\n" +
        "B	8.0.2148	Hotfix	Hotfix	901212	2006-07-25		FIX: You receive an error message if you use the sp_addalias or sp_dropalias procedures when the IMPLICIT_TRANSACTIONS option is set to ON in SQL Server 2000 SP4\n" +
        "B	8.0.2148	SP4	Hotfix	902150	2006-06-01		FIX: Some 32-bit applications that use SQL-DMO and SQL-VDI APIs may stop working after you install SQL Server 2000 Service Pack 4 on an Itanium-based computer\n" +
        "B	8.0.2148	Hotfix	Hotfix	902955	2006-07-25		FIX: You receive a \"Getting registry information\" message when you run the Sqldiag.exe utility after you install SQL Server 2000 SP4\n" +
        "B	8.0.2147	Hotfix	Hotfix	899410	2006-06-01		FIX: You may experience slow server performance when you start a trace in an instance of SQL Server 2000 that runs on a computer that has more than four processors\n" +
        "B	8.0.2145	Hotfix	Hotfix	826906	2005-10-25		FIX: A query that uses a view that contains a correlated subquery and an aggregate runs slowly\n" +
        "B	8.0.2145	Hotfix	Hotfix	836651	2006-06-07		FIX: You receive query results that were not expected when you use both ANSI joins and non-ANSI joins\n" +
        "B	8.0.2066		Unknown		2012-08-14		Microsoft Security Bulletin MS12-060\n" +
        "B	8.0.2065	RTM + security update	SecurityUpdate	983808	2012-04-10		MS12-027: Description of the security update for Microsoft SQL Server 2000 Service Pack 4 GDR: April 10, 2012\n" +
        "B	8.0.2055		Unknown	959420	2009-02-10		MS09-004: Vulnerabilities in Microsoft SQL Server could allow remote code execution\n" +
        "B	8.0.2050	RTM + security update	SecurityUpdate	948110	2008-07-08		MS08-040: Description of the security update for SQL Server 2000 GDR and MSDE 2000: July 8, 2008\n" +
        "B	8.0.2040	Hotfix	Hotfix	899761	2006-08-15		FIX: Not all memory is available when AWE is enabled on a computer that is running a 32-bit version of SQL Server 2000 SP4\n" +
        "B	8.0.2039	SP4	ServicePack		2005-05-06		SQL Server 2000 Service Pack 4 (SP4)\n" +
        "B	8.0.2026	SP4	Preview				SQL Server 2000 Service Pack 4 (SP4) Beta Beta\n" +
        "B	8.0.1547	Hotfix	Hotfix	899410	2006-06-01		FIX: You may experience slow server performance when you start a trace in an instance of SQL Server 2000 that runs on a computer that has more than four processors\n" +
        "B	8.0.1077	SP2	SecurityUpdate	983814	2012-10-09		MS12-070: Description of the security update for SQL Server 2000 Reporting Services Service Pack 2\n" +
        "B	8.0.1037	Hotfix	Hotfix	930484	2007-02-02		FIX: CPU utilization may approach 100 percent on a computer that is running SQL Server 2000 after you run the BACKUP DATABASE statement or the BACKUP LOG statement\n" +
        "B	8.0.1036	Hotfix	Hotfix	929410	2007-01-11		FIX: Error message when you run a full-text query in SQL Server 2000: \"Error: 17883, Severity: 1, State: 0\"\n" +
        "B	8.0.1035	Hotfix	Hotfix	917593	2006-09-22		FIX: The \"Audit Logout\" event does not appear in the trace results file when you run a profiler trace against a linked server instance in SQL Server 2000\n" +
        "B	8.0.1034	Hotfix	Hotfix	915328	2006-08-09		FIX: You may intermittently experience an access violation error when a query is executed in a parallel plan and the execution plan contains either a HASH JOIN operation or a Sort operation in SQL Server 2000\n" +
        "B	8.0.1029	Hotfix	Hotfix	902852	2006-06-01		FIX: Error message when you run an UPDATE statement that uses two JOIN hints to update a table in SQL Server 2000: \"Internal SQL Server error\"\n" +
        "B	8.0.1027	Hotfix	Hotfix	900416	2006-07-25		FIX: A 17883 error may occur you run a query that uses a hash join in SQL Server 2000\n" +
        "B	8.0.1025	Hotfix	Hotfix	899428	2006-06-01		FIX: You receive incorrect results when you run a query that uses a cross join operator in SQL Server 2000 SP3\n" +
        "B	8.0.1025	Hotfix	Hotfix	899430	2006-07-25		FIX: An access violation may occur when you run a SELECT query and the NO_BROWSETABLE option is set to ON in Microsoft SQL Server 2000\n" +
        "B	8.0.1024	Hotfix	Hotfix	898709	2006-07-25		FIX: Error message when you use SQL Server 2000: \"Time out occurred while waiting for buffer latch type 3\"\n" +
        "B	8.0.1021	Hotfix	Hotfix	887700	2006-07-25		FIX: Server Network Utility may display incorrect protocol properties in SQL Server 2000\n" +
        "B	8.0.1020	Hotfix	Hotfix	896985	2006-07-25		FIX: The Subscriber may not be able to upload changes to the Publisher when you incrementally add an article to a publication in SQL Server 2000 SP3\n" +
        "B	8.0.1019	Hotfix	Hotfix	897572	2006-06-01		FIX: You may receive a memory-related error message when you repeatedly create and destroy an out-of-process COM object within the same batch or stored procedure in SQL Server 2000\n" +
        "B	8.0.1017	Hotfix	Hotfix	896425	2006-06-01		FIX: The BULK INSERT statement silently skips insert attempts when the data value is NULL and the column is defined as NOT NULL for INT, SMALLINT, and BIGINT data types in SQL Server 2000\n" +
        "B	8.0.1014	Hotfix	Hotfix	895123	2006-06-01		FIX: You may receive error message 701, error message 802, and error message 17803 when many hashed buffers are available in SQL Server 2000\n" +
        "B	8.0.1014	Hotfix	Hotfix	895187	2006-07-25		FIX: You receive an error message when you try to delete records by running a Delete Transact-SQL statement in SQL Server 2000\n" +
        "B	8.0.1013	Hotfix	Hotfix	891866	2006-06-01		FIX: The query runs slower than you expected when you try to parse a query in SQL Server 2000\n" +
        "B	8.0.1009	Hotfix	Hotfix	894257	2006-06-01		FIX: You receive an \"Incorrect syntax near ')'\" error message when you run a script that was generated by SQL-DMO for an Operator object in SQL Server 2000\n" +
        "B	8.0.1007	Hotfix	Hotfix	893312	2006-06-01		FIX: You may receive a \"SQL Server could not spawn process_loginread thread\" error message, and a memory leak may occur when you cancel a remote query in SQL Server 2000\n" +
        "B	8.0.1003	Hotfix	Hotfix	892923	2006-06-01		FIX: Differential database backups may not contain database changes in the Page Free Space (PFS) pages in SQL Server 2000\n" +
        "B	8.0.1001	Hotfix	Hotfix	892205	2006-06-01		FIX: You may receive a 17883 error message when SQL Server 2000 performs a very large hash operation\n" +
        "B	8.0.1000	Hotfix	Hotfix	891585	2006-06-01		FIX: Database recovery does not occur, or a user database is marked as suspect in SQL Server 2000\n" +
        "B	8.0.997	Hotfix	Hotfix	891311	2006-07-18		FIX: You cannot create new TCP/IP socket based connections after error messages 17882 and 10055 are written to the Microsoft SQL Server 2000 error log\n" +
        "B	8.0.996	Hotfix	Hotfix	891017	2006-06-01		FIX: SQL Server 2000 may stop responding to other requests when you perform a large deallocation operation\n" +
        "B	8.0.996	Hotfix	Hotfix	891268	2006-06-01		FIX: You receive a 17883 error message and SQL Server 2000 may stop responding to other requests when you perform large in-memory sort operations\n" +
        "B	8.0.994	SP2	Hotfix	890942	2006-06-01		FIX: Some complex queries are slower after you install SQL Server 2000 Service Pack 2 or SQL Server 2000 Service Pack 3\n" +
        "B	8.0.994	Hotfix	Hotfix	890768	2006-06-01		FIX: You experience non-convergence in a replication topology when you unpublish or drop columns from a dynamically filtered publication in SQL Server 2000\n" +
        "B	8.0.994	Hotfix	Hotfix	890767	2006-06-01		FIX: You receive a \"Server: Msg 107, Level 16, State 3, Procedure TEMP_VIEW_Merge, Line 1\" error message when the sum of the length of the published column names in a merge publication exceeds 4,000 characters in SQL Server 2000\n" +
        "B	8.0.993	Hotfix	Hotfix	890925	2006-06-01		FIX: The @@ERROR system function may return an incorrect value when you execute a Transact-SQL statement that uses a parallel execution plan in SQL Server 2000 32-bit or in SQL Server 2000 64-bit\n" +
        "B	8.0.993	SP3	Hotfix	888444	2006-06-01		FIX: You receive a 17883 error in SQL Server 2000 Service Pack 3 or in SQL Server 2000 Service Pack 3a when a worker thread becomes stuck in a registry call\n" +
        "B	8.0.993	Hotfix	Hotfix	890742	2006-05-15		FIX: Error message when you use a loopback linked server to run a distributed query in SQL Server 2000: \"Could not perform the requested operation because the minimum query memory is not available\"\n" +
        "B	8.0.991	Hotfix	Hotfix	889314	2006-06-01		FIX: Non-convergence may occur in a merge replication topology if the primary connection to the publisher is disconnected\n" +
        "B	8.0.990	Hotfix	Hotfix	890200	2006-06-01		FIX: SQL Server 2000 stops listening for new TCP/IP Socket connections unexpectedly after error message 17882 is written to the SQL Server 2000 error log\n" +
        "B	8.0.988	Hotfix	Hotfix	889166	2006-06-01		FIX: You receive a \"Msg 3628\" error message when you run an inner join query in SQL Server 2000\n" +
        "B	8.0.985	Hotfix	Hotfix	889239	2006-06-01		FIX: Start times in the SQL Profiler are different for the Audit:Login and Audit:Logout Events in SQL Server 2000\n" +
        "B	8.0.980	SP3	Hotfix	887974	2006-06-01		FIX: A fetch on a dynamic cursor can cause unexpected results in SQL Server 2000 Service Pack 3\n" +
        "B	8.0.977	Hotfix	Hotfix	888007	2005-08-31		You receive a \"The product does not have a prerequisite update installed\" error message when you try to install a SQL Server 2000 post-Service Pack 3 hotfix\n" +
        "B	8.0.973	Hotfix	Hotfix	884554	2006-06-01		FIX: A SPID stops responding with a NETWORKIO (0x800) waittype in SQL Server Enterprise Manager when SQL Server tries to process a fragmented TDS network packet\n" +
        "B	8.0.972	Hotfix	Hotfix	885290	2006-06-01		FIX: An assertion error occurs when you insert data in the same row in a table by using multiple connections to an instance of SQL Server\n" +
        "B	8.0.970	Hotfix	Hotfix	872842	2006-06-01		FIX: A CHECKDB statement reports a 2537 corruption error after SQL Server transfers data to a sql_variant column in SQL Server 2000\n" +
        "B	8.0.967	Hotfix	Hotfix	878501	2006-06-01		FIX: You may receive an error message when you run a SET IDENTITY_INSERT ON statement on a table and then try to insert a row into the table in SQL Server 2000\n" +
        "B	8.0.962	Hotfix	Hotfix	883415	2006-06-01		FIX: A user-defined function returns results that are not correct for a query\n" +
        "B	8.0.961	Hotfix	Hotfix	873446	2006-06-01		FIX: An access violation exception may occur when multiple users try to perform data modification operations at the same time that fire triggers that reference a deleted or an inserted table in SQL Server 2000 on a computer that is running SMP\n" +
        "B	8.0.959	Hotfix	Hotfix	878500	2006-06-01		FIX: An Audit Object Permission event is not produced when you run a TRUNCATE TABLE statement\n" +
        "B	8.0.957	Hotfix	Hotfix	870994	2006-06-01		FIX: An access violation exception may occur when you run a query that uses index names in the WITH INDEX option to specify an index hint\n" +
        "B	8.0.955	Hotfix	Hotfix	867798	2007-01-08		FIX: The @date_received parameter of the xp_readmail extended stored procedure incorrectly returns the date and the time that an e-mail message is submitted by the sender in SQL Server 2000\n" +
        "B	8.0.954	Hotfix	Hotfix	843282	2007-01-05		FIX: The Osql.exe utility does not run a Transact-SQL script completely if you start the program from a remote session by using a background service and then log off the console session\n" +
        "B	8.0.952	Hotfix	Hotfix	867878	2006-06-01		FIX: The Log Reader Agent may cause 17883 error messages\n" +
        "B	8.0.952	Hotfix	Hotfix	867879	2006-06-01		FIX: Merge replication non-convergence occurs with SQL Server CE subscribers\n" +
        "B	8.0.952	Hotfix	Hotfix	867880	2006-06-01		FIX: Merge Agent may fail with an \"Invalid character value for cast specification\" error message\n" +
        "B	8.0.949	SP3	Hotfix	843266	2006-06-02		FIX: Shared page locks can be held until end of the transaction and can cause blocking or performance problems in SQL Server 2000 Service Pack 3 (SP3)\n" +
        "B	8.0.948	Hotfix	Hotfix	843263	2006-06-01		FIX: You may receive an 8623 error message when you try to run a complex query on an instance of SQL Server\n" +
        "B	8.0.944	Hotfix	Hotfix	839280	2006-06-05		FIX: SQL debugging does not work in Visual Studio .NET after you install Windows XP Service Pack 2\n" +
        "B	8.0.937	Hotfix	Hotfix	841776	2006-06-01		FIX: Additional diagnostics have been added to SQL Server 2000 to detect unreported read operation failures\n" +
        "B	8.0.936	Hotfix	Hotfix	841627	2006-06-01		FIX: SQL Server 2000 may underestimate the cardinality of a query expression under certain circumstances\n" +
        "B	8.0.935	Hotfix	Hotfix	841401	2006-06-01		FIX: You may notice incorrect values for the \"Active Transactions\" counter when you perform multiple transactions on an instance of SQL Server 2000 that is running on an SMP computer\n" +
        "B	8.0.934	Hotfix	Hotfix	841404	2006-06-01		FIX: You may receive a \"The query processor could not produce a query plan\" error message in SQL Server when you run a query that includes multiple subqueries that use self-joins\n" +
        "B	8.0.933	SP3	Hotfix	840856	2006-06-02		FIX: The MSSQLServer service exits unexpectedly in SQL Server 2000 Service Pack 3\n" +
        "B	8.0.929	Hotfix	Hotfix	839529	2006-06-01		FIX: 8621 error conditions may cause SQL Server 2000 64-bit to close unexpectedly\n" +
        "B	8.0.928	Hotfix	Hotfix	839589	2006-06-01		FIX: The thread priority is raised for some threads in a parallel query\n" +
        "B	8.0.927	Hotfix	Hotfix	839688	2006-06-01		FIX: Profiler RPC events truncate parameters that have a text data type to 16 characters\n" +
        "B	8.0.926	Hotfix	Hotfix	839523	2006-06-01		FIX: An access violation exception may occur when you update a text column by using a stored procedure in SQL Server 2000\n" +
        "B	8.0.923	Hotfix	Hotfix	838460	2006-06-01		FIX: The xp_logininfo procedure may fail with error 8198 after you install Q825042 or any hotfix with SQL Server 8.0.0840 or later\n" +
        "B	8.0.922	Hotfix	Hotfix	837970	2005-10-25		FIX: You may receive an \"Invalid object name...\" error message when you run the DBCC CHECKCONSTRAINTS Transact-SQL statement on a table in SQL Server 2000\n" +
        "B	8.0.919	Hotfix	Hotfix	837957	2005-10-25		FIX: When you use Transact-SQL cursor variables to perform operations that have large iterations, memory leaks may occur in SQL Server 2000\n" +
        "B	8.0.916	Hotfix	Hotfix	317989	2005-09-27		FIX: Sqlakw32.dll May Corrupt SQL Statements\n" +
        "B	8.0.915	Hotfix	Hotfix	837401	2005-10-25		FIX: Rows are not successfully inserted into a table when you use the BULK INSERT command to insert rows\n" +
        "B	8.0.913	Hotfix	Hotfix	836651	2006-06-07		FIX: You receive query results that were not expected when you use both ANSI joins and non-ANSI joins\n" +
        "B	8.0.911	Hotfix	Hotfix	837957	2005-10-25		FIX: When you use Transact-SQL cursor variables to perform operations that have large iterations, memory leaks may occur in SQL Server 2000\n" +
        "B	8.0.910	Hotfix	Hotfix	834798	2005-10-25		FIX: SQL Server 2000 may not start if many users try to log in to SQL Server when SQL Server is trying to start\n" +
        "B	8.0.908	Hotfix	Hotfix	834290	2005-10-25		FIX: You receive a 644 error message when you run an UPDATE statement and the isolation level is set to READ UNCOMMITTED\n" +
        "B	8.0.904	Hotfix	Hotfix	834453	2005-04-22		FIX: The Snapshot Agent may fail after you make schema changes to the underlying tables of a publication\n" +
        "B	8.0.892	Hotfix	Hotfix	833710	2005-10-25		FIX: You receive an error message when you try to restore a database backup that spans multiple devices\n" +
        "B	8.0.891	Hotfix	Hotfix	836141	2005-04-01		FIX: An access violation exception may occur when SQL Server runs many parallel query processing operations on a multiprocessor computer\n" +
        "B	8.0.879	Hotfix	Hotfix	832977	2005-10-25		FIX: The DBCC PSS Command may cause access violations and 17805 errors in SQL Server 2000\n" +
        "B	8.0.878	Hotfix	Hotfix	831950	2005-10-25		FIX: You receive error message 3456 when you try to apply a transaction log to a server\n" +
        "B	8.0.876	Hotfix	Hotfix	830912	2005-10-25		FIX: Key Names Read from an .Ini File for a Dynamic Properties Task May Be Truncated\n" +
        "B	8.0.876	Hotfix	Hotfix	831997	2005-10-25		FIX: An invalid cursor state occurs after you apply Hotfix 8.00.0859 or later in SQL Server 2000\n" +
        "B	8.0.876	Hotfix	Hotfix	831999	2005-10-25		FIX: An AWE system uses more memory for sorting or for hashing than a non-AWE system in SQL Server 2000\n" +
        "B	8.0.873	Hotfix	Hotfix	830887	2005-10-25		FIX: Some queries that have a left outer join and an IS NULL filter run slower after you install SQL Server 2000 post-SP3 hotfix\n" +
        "B	8.0.871	Hotfix	Hotfix	830767	2005-10-25		FIX: SQL Query Analyzer may stop responding when you close a query window or open a file\n" +
        "B	8.0.871	Hotfix	Hotfix	830860	2005-10-25		FIX: The performance of a computer that is running SQL Server 2000 degrades when query execution plans against temporary tables remain in the procedure cache\n" +
        "B	8.0.870	Hotfix	Hotfix	830262	2005-10-25		FIX: Unconditional Update May Not Hold Key Locks on New Key Values\n" +
        "B	8.0.869	Hotfix	Hotfix	830588	2005-10-25		FIX: Access violation when you trace keyset-driven cursors by using SQL Profiler\n" +
        "B	8.0.866	Hotfix	SecurityUpdate	830366	2006-01-16		FIX: An access violation occurs in SQL Server 2000 when a high volume of local shared memory connections occur after you install security update MS03-031\n" +
        "B	8.0.865	Hotfix	Hotfix	830395	2005-10-25		FIX: An access violation occurs during compilation if the table contains statistics for a computed column\n" +
        "B	8.0.865	Hotfix	Hotfix	828945	2005-10-25		FIX: You cannot insert explicit values in an IDENTITY column of a SQL Server table by using the SQLBulkOperations function or the SQLSetPos ODBC function in SQL Server 2000\n" +
        "B	8.0.863	Hotfix	Hotfix	829205	2005-10-25		FIX: Query performance may be slow and may be inconsistent when you run a query while another query that contains an IN operator with many values is compiled\n" +
        "B	8.0.863	Hotfix	Hotfix	829444	2005-10-25		FIX: A floating point exception occurs during the optimization of a query\n" +
        "B	8.0.859	Hotfix	Hotfix	821334	2005-03-31		FIX: Issues that are resolved in SQL Server 2000 build 8.00.0859\n" +
        "B	8.0.858	Hotfix	Hotfix	828637	2005-10-25		FIX: Users Can Control the Compensating Change Process in Merge Replication\n" +
        "B	8.0.857		Unknown	828017			The Knowledge Base (KB) Article You Requested Is Currently Not Available\n" +
        "B	8.0.857	Hotfix	Hotfix	827714	2005-11-23		FIX: A query may fail with retail assertion when you use the NOLOCK hint or the READ UNCOMMITTED isolation level\n" +
        "B	8.0.857	Hotfix	Hotfix	828308	2005-10-25		FIX: An Internet Explorer script error occurs when you access metadata information by using DTS in SQL Server Enterprise Manager\n" +
        "B	8.0.856	Hotfix	Hotfix	828096	2005-10-25		FIX: Key Locks Are Held Until the End of the Statement for Rows That Do Not Pass Filter Criteria\n" +
        "B	8.0.854	Hotfix	Hotfix	828699	2005-10-25		FIX: An Access Violation Occurs When You Run DBCC UPDATEUSAGE on a Database That Has Many Objects\n" +
        "B	8.0.852	Hotfix	Hotfix	830466	2005-04-01		FIX: You may receive an \"Internal SQL Server error\" error message when you run a Transact-SQL SELECT statement on a view that has many subqueries in SQL Server 2000\n" +
        "B	8.0.852	Hotfix	Hotfix	827954	2005-10-25		FIX: Slow Execution Times May Occur When You Run DML Statements Against Tables That Have Cascading Referential Integrity\n" +
        "B	8.0.851	Hotfix	Hotfix	826754	2005-10-25		FIX: A Deadlock Occurs If You Run an Explicit UPDATE STATISTICS Command\n" +
        "B	8.0.850	Hotfix	Hotfix	826860	2005-10-25		FIX: Linked Server Query May Return NULL If It Is Performed Through a Keyset Cursor\n" +
        "B	8.0.850	Hotfix	Hotfix	826815	2005-10-25		FIX: You receive an 8623 error message in SQL Server when you try to run a query that has multiple correlated subqueries\n" +
        "B	8.0.850	Hotfix	Hotfix	826906	2005-10-25		FIX: A query that uses a view that contains a correlated subquery and an aggregate runs slowly\n" +
        "B	8.0.848	Hotfix	Hotfix	826822	2005-10-25		FIX: A Member of the db_accessadmin Fixed Database Role Can Create an Alias for the dbo Special User\n" +
        "B	8.0.847		Unknown	826433	2005-10-25		PRB: Additional SQL Server Diagnostics Added to Detect Unreported I/O Problems\n" +
        "B	8.0.845	Hotfix	Hotfix	826364	2005-10-05		FIX: A Query with a LIKE Comparison Results in a Non-Optimal Query Plan When You Use a Hungarian SQL Server Collation\n" +
        "B	8.0.845	Hotfix	Hotfix	825854	2005-10-25		FIX: No Exclusive Locks May Be Taken If the DisAllowsPageLocks Value Is Set to True\n" +
        "B	8.0.844	Hotfix	Hotfix	826080	2006-10-17		FIX: SQL Server 2000 protocol encryption applies to JDBC clients\n" +
        "B	8.0.842	Hotfix	Hotfix	825043	2005-10-25		FIX: Rows are unexpectedly deleted when you run a distributed query to delete or to update a linked server table\n" +
        "B	8.0.841	Hotfix	Hotfix	825225	2005-10-25		FIX: You receive an error message when you run a parallel query that uses an aggregation function or the GROUP BY clause\n" +
        "B	8.0.840	Hotfix	Hotfix	319477	2005-09-27		FIX: Extremely Large Number of User Tables on AWE System May Cause BPool::Map Errors\n" +
        "B	8.0.840	Hotfix	Hotfix	319477	2005-09-27		FIX: Extremely Large Number of User Tables on AWE System May Cause BPool::Map Errors\n" +
        "B	8.0.839	Hotfix	Hotfix	823877	2005-10-25		FIX: An Access Violation May Occur When You Run a Query That Contains 32,000 or More OR Clauses\n" +
        "B	8.0.839	Hotfix	Hotfix	824027	2005-10-25		FIX: A Cursor with a Large Object Parameter May Cause an Access Violation on CStmtCond::XretExecute\n" +
        "B	8.0.837	Hotfix	Hotfix	820788	2005-10-25		FIX: Delayed domain authentication may cause SQL Server to stop responding\n" +
        "B	8.0.837	Hotfix	Hotfix	821741	2005-10-25		FIX: Lock monitor exception in DeadlockMonitor::ResolveDeadlock\n" +
        "B	8.0.837	Hotfix	Hotfix	821548	2005-10-25		FIX: A Parallel Query May Generate an Access Violation After You Install SQL Server 2000 SP3\n" +
        "B	8.0.837	Hotfix	Hotfix	821740	2005-10-25		FIX: MS DTC Transaction Commit Operation Blocks Itself\n" +
        "B	8.0.837	Hotfix	Hotfix	823514	2005-10-25		FIX: Build 8.0.0837: A query that contains a correlated subquery runs slowly\n" +
        "B	8.0.819	Hotfix	Hotfix	826161	2005-10-25		FIX: You are prompted for password confirmation after you change a standard SQL Server login\n" +
        "B	8.0.818	SP3	ServicePack	821277	2006-01-09		MS03-031: Security patch for SQL Server 2000 Service Pack 3\n" +
        "B	8.0.818	Hotfix	Hotfix	821337	2005-03-16		FIX: Localized versions of SQL Mail and the Web Assistant Wizard may not work as expected in SQL Server 2000 64 bit\n" +
        "B	8.0.818	Hotfix	Hotfix	818388	2005-02-10		FIX: A Transact-SQL Statement That Is Embedded in the Database Name Runs with System Administrator Permissions\n" +
        "B	8.0.818	Hotfix	Hotfix	826161	2005-10-25		FIX: You are prompted for password confirmation after you change a standard SQL Server login\n" +
        "B	8.0.818		Unknown	821280	2006-03-14		MS03-031: Security Patch for SQL Server 2000 64-bit\n" +
        "B	8.0.816	Hotfix	Hotfix	818766	2005-10-25		FIX: Intense SQL Server activity results in spinloop wait\n" +
        "B	8.0.814	Hotfix	Hotfix	819662	2005-10-25		FIX: Distribution Cleanup Agent Incorrectly Cleans Up Entries for Anonymous Subscribers\n" +
        "B	8.0.811	Hotfix	Hotfix	819248	2006-04-03		FIX: An access violation exception may occur when you insert a row in a table that is referenced by indexed views in SQL Server 2000\n" +
        "B	8.0.811	Hotfix	Hotfix	819662	2005-10-25		FIX: Distribution Cleanup Agent Incorrectly Cleans Up Entries for Anonymous Subscribers\n" +
        "B	8.0.811	Hotfix	Hotfix	818897	2005-10-25		FIX: Invalid TDS Sent to SQL Server Results in Access Violation\n" +
        "B	8.0.807	Hotfix	Hotfix	818899	2005-10-25		FIX: Error Message 3628 May Occur When You Run a Complex Query\n" +
        "B	8.0.804	Hotfix	Hotfix	818729	2005-10-25		FIX: Internal Query Processor Error 8623 When Microsoft SQL Server Tries to Compile a Plan for a Complex Query\n" +
        "B	8.0.801	Hotfix	Hotfix	818540	2006-01-26		FIX: SQL Server Enterprise Manager unexpectedly quits when you modify a DTS package\n" +
        "B	8.0.800	Hotfix	Hotfix	818414	2005-09-27		FIX: The Sqldumper.exe File Does Not Generate a Userdump File When It Runs Against a Windows Service\n" +
        "B	8.0.800	Hotfix	Hotfix	818097	2005-09-27		FIX: An Access Violation May Occur When You Run DBCC DBREINDEX on a Table That Has Hypothetical Indexes\n" +
        "B	8.0.800	Hotfix	Hotfix	818188	2005-09-27		FIX: Query on the sysmembers Virtual Table May Fail with a Stack Overflow\n" +
        "B	8.0.798	Hotfix	Hotfix	817464	2005-09-27		FIX: Using Sp_executesql in Merge Agent Operations\n" +
        "B	8.0.794	Hotfix	Hotfix	817464	2005-09-27		FIX: Using Sp_executesql in Merge Agent Operations\n" +
        "B	8.0.794	Hotfix	Hotfix	813524	2005-09-27		FIX: OLE DB conversion errors may occur after you select a literal string that represents datetime data as a column\n" +
        "B	8.0.794	Hotfix	Hotfix	816440	2005-09-27		FIX: Error 8623 is Raised When SQL Server Compiles a Complex Query\n" +
        "B	8.0.794	Hotfix	Hotfix	817709	2005-02-11		FIX: SQL Server 2000 might produce an incorrect cardinality estimate for outer joins\n" +
        "B	8.0.791	Hotfix	Hotfix	815249	2005-09-27		FIX: Performance of a query that is run from a client program on a SQL Server SP3 database is slow after you restart the instance of SQL Server\n" +
        "B	8.0.790	Hotfix	Hotfix	817081	2005-09-27		FIX: You receive an error message when you use the SQL-DMO BulkCopy object to import data into a SQL Server table\n" +
        "B	8.0.789	Hotfix	Hotfix	816840	2005-09-27		FIX: Error 17883 May Display Message Text That Is Not Correct\n" +
        "B	8.0.788	Hotfix	Hotfix	816985	2005-09-27		FIX: You cannot install SQL Server 2000 SP3 on the Korean version of SQL Server 2000\n" +
        "B	8.0.781	Hotfix	Hotfix	815057	2005-09-27		FIX: SQL Server 2000 Uninstall Option Does Not Remove All Files\n" +
        "B	8.0.780	Hotfix	Hotfix	816039	2005-09-27		FIX: Code Point Comparison Semantics for SQL_Latin1_General_Cp850_BIN Collation\n" +
        "B	8.0.780	Hotfix	Hotfix	816084	2005-09-27		FIX: sysindexes.statblob Column May Be Corrupted After You Run a DBCC DBREINDEX Statement\n" +
        "B	8.0.780	SP3	Hotfix	810185	2006-10-10		SQL Server 2000 hotfix update for SQL Server 2000 Service Pack 3 and 3a\n" +
        "B	8.0.779	SP3	Hotfix	814035	2005-09-27		FIX: A Full-Text Population Fails After You Apply SQL Server 2000 Service Pack 3\n" +
        "B	8.0.776		Unknown				Unidentified\n" +
        "B	8.0.775	Hotfix	Hotfix	815115	2005-09-27		FIX: A DTS package that uses global variables ignores an error message raised by RAISERROR\n" +
        "B	8.0.769	Hotfix	Hotfix	814889	2005-09-27		FIX: A DELETE statement with a JOIN might fail and you receive a 625 error\n" +
        "B	8.0.769	Hotfix	Hotfix	814893	2005-09-27		FIX: Error Message: \"Insufficient key column information for updating\" Occurs in SQL Server 2000 SP3\n" +
        "B	8.0.765	Hotfix	Hotfix	810163	2005-09-27		FIX: An Access Violation Occurs if an sp_cursoropen Call References a Parameter That Is Not Defined\n" +
        "B	8.0.765	Hotfix	Hotfix	810688	2005-09-27		FIX: Merge Agent Can Resend Changes for Filtered Publications\n" +
        "B	8.0.765	Hotfix	Hotfix	811611	2005-09-27		FIX: Reinitialized SQL Server CE 2.0 subscribers may experience data loss and non-convergence\n" +
        "B	8.0.765	Hotfix	Hotfix	813769	2005-09-27		FIX: You May Experience Slow Performance When You Debug a SQL Server Service\n" +
        "B	8.0.763	SP3	Hotfix	814113	2005-09-27		FIX: DTS Designer may generate an access violation after you install SQL Server 2000 Service Pack 3\n" +
        "B	8.0.762	SP3	Hotfix	814032	2005-09-27		FIX: Merge publications cannot synchronize on SQL Server 2000 Service Pack 3\n" +
        "B	8.0.760	SP3	ServicePack		2003-08-27		SQL Server 2000 Service Pack 3 (SP3 / SP3a)\n" +
        "B	8.0.743	Hotfix	Hotfix	818406	2005-10-18		FIX: A Transact-SQL query that uses views may fail unexpectedly in SQL Server 2000 SP2\n" +
        "B	8.0.743	SP2	Hotfix	818763	2005-10-25		FIX: Intense SQL Server Activity Results in Spinloop Wait in SQL Server 2000 Service Pack 2\n" +
        "B	8.0.741	Hotfix	Hotfix	818096	2005-02-10		FIX: Many Extent Lock Time-outs May Occur During Extent Allocation\n" +
        "B	8.0.736	Hotfix	Hotfix	816937	2005-09-27		FIX: A memory leak may occur when you use the sp_OAMethod stored procedure to call a method of a COM object\n" +
        "B	8.0.735	Hotfix	Hotfix	814889	2005-09-27		FIX: A DELETE statement with a JOIN might fail and you receive a 625 error\n" +
        "B	8.0.733	Hotfix	Hotfix	813759	2005-09-27		FIX: A Large Number of NULL Values in Join Columns Result in Slow Query Performance\n" +
        "B	8.0.730	Hotfix	Hotfix	813769	2005-09-27		FIX: You May Experience Slow Performance When You Debug a SQL Server Service\n" +
        "B	8.0.728	Hotfix	Hotfix	814460	2005-09-27		FIX: Merge Replication with Alternate Synchronization Partners May Not Succeed After You Change the Retention Period\n" +
        "B	8.0.725	Hotfix	Hotfix	812995	2005-09-27		FIX: A Query with an Aggregate Function May Fail with a 3628 Error\n" +
        "B	8.0.725	Hotfix	Hotfix	813494	2005-09-27		FIX: Distribution Agent Fails with \"Violation of Primary Key Constraint\" Error Message\n" +
        "B	8.0.723	Hotfix	Hotfix	812798	2005-09-27		FIX: A UNION ALL View May Not Use Index If Partitions Are Removed at Compile Time\n" +
        "B	8.0.721	Hotfix	Hotfix	812250	2005-09-27		FIX: Indexed View May Cause a Handled Access Violation in CIndex::SetLevel1Names\n" +
        "B	8.0.721	Hotfix	Hotfix	812393	2005-09-27		FIX: Update or Delete Statement Fails with Error 1203 During Row Lock Escalation\n" +
        "B	8.0.718	Hotfix	Hotfix	811703	2005-09-27		FIX: Unexpected results from partial aggregations based on conversions\n" +
        "B	8.0.715	Hotfix	Hotfix	810688	2005-09-27		FIX: Merge Agent Can Resend Changes for Filtered Publications\n" +
        "B	8.0.715	Hotfix	Hotfix	811611	2005-09-27		FIX: Reinitialized SQL Server CE 2.0 subscribers may experience data loss and non-convergence\n" +
        "B	8.0.714	SP2	Hotfix	811478	2005-10-18		FIX: Restoring a SQL Server 7.0 database backup in SQL Server 2000 Service Pack 2 (SP2) may cause an assertion error in the Xdes.cpp file\n" +
        "B	8.0.713	Hotfix	Hotfix	811205	2005-09-27		FIX: An error message occurs when you perform a database or a file SHRINK operation\n" +
        "B	8.0.710	Hotfix	Hotfix	811052	2005-09-27		FIX: Latch Time-Out Message 845 Occurs When You Perform a Database or File SHRINK Operation\n" +
        "B	8.0.705	Hotfix	Hotfix	810920	2005-09-27		FIX: The JOIN queries in the triggers that involve the inserted table or the deleted table may return results that are not consistent\n" +
        "B	8.0.703	Hotfix	Hotfix	810526	2005-09-27		FIX: Cursors That Have a Long Lifetime May Cause Memory Fragmentation\n" +
        "B	8.0.702	Hotfix	Hotfix	328551	2006-07-19		FIX: Concurrency enhancements for the tempdb database\n" +
        "B	8.0.701	Hotfix	Hotfix	810026	2005-09-27		FIX: A DELETE Statement with a Self-Join May Fail and You Receive a 625 Error\n" +
        "B	8.0.701	Hotfix	Hotfix	810163	2005-09-27		FIX: An Access Violation Occurs if an sp_cursoropen Call References a Parameter That Is Not Defined\n" +
        "B	8.0.700	Hotfix	Hotfix	810072	2005-09-27		FIX: Merge Replication Reconciler Stack Overflow\n" +
        "B	8.0.696	Hotfix	Hotfix	810052	2005-09-27		FIX: A Memory Leak Occurs When Cursors Are Opened During a Connection\n" +
        "B	8.0.696	Hotfix	Hotfix	810010	2005-09-27		FIX: The fn_get_sql System Table Function May Cause Various Handled Access Violations\n" +
        "B	8.0.695	Hotfix	Hotfix	331885	2005-09-27		FIX: Update/Delete Statement Fails with Error 1203 During Page Lock Escalation\n" +
        "B	8.0.695	Hotfix	Hotfix	331965	2005-02-10		FIX: The xp_readmail Extended Stored Procedure Overwrites Attachment That Already Exists\n" +
        "B	8.0.695	Hotfix	Hotfix	331968	2005-02-10		FIX: The xp_readmail and xp_findnextmsg Extended Stored Procedures Do Not Read Mail in Time Received Order\n" +
        "B	8.0.693	Hotfix	Hotfix	330212	2005-09-27		FIX: Parallel logical operation returns results that are not consistent\n" +
        "B	8.0.690	Hotfix	Hotfix	311104	2005-10-12		FIX: The SELECT Statement with Parallelism Enabled May Cause an Assertion\n" +
        "B	8.0.689	Hotfix	Hotfix	329499	2005-10-11		FIX: Replication Removed from Database After Restore WITH RECOVERY\n" +
        "B	8.0.688	Hotfix	Hotfix	329487	2005-10-11		FIX: Transaction Log Restore Fails with Message 3456\n" +
        "B	8.0.686	SP2	ServicePack	316333	2006-11-24		SQL Server 2000 Security Update for Service Pack 2\n" +
        "B	8.0.682	Hotfix	Hotfix	319851	2005-10-18		FIX: Assertion and Error Message 3314 Occurs If You Try to Roll Back a Text Operation with READ UNCOMMITTED\n" +
        "B	8.0.679	SP2	ServicePack	316333	2006-11-24		SQL Server 2000 Security Update for Service Pack 2\n" +
        "B	8.0.678	Hotfix	Hotfix	328354	2005-09-27		FIX: A RESTORE DATABASE WITH RECOVERY Statement Can Fail with Error 9003 or Error 9004\n" +
        "B	8.0.667		Unknown				2000 SP2+8/14 fix\n" +
        "B	8.0.665		Unknown				2000 SP2+8/8 fix\n" +
        "B	8.0.661	Hotfix	Hotfix	326999	2005-09-27		FIX: Lock escalation on a scan while an update query is running causes a 1203 error message to occur\n" +
        "B	8.0.655		Unknown				2000 SP2+7/24 fix\n" +
        "B	8.0.652	Hotfix	Hotfix	810010	2005-09-27		FIX: The fn_get_sql System Table Function May Cause Various Handled Access Violations\n" +
        "B	8.0.650	Hotfix	Hotfix	322853	2003-11-05		FIX: SQL Server Grants Unnecessary Permissions or an Encryption Function Contains Unchecked Buffers\n" +
        "B	8.0.644	Hotfix	Hotfix	324186	2005-09-27		FIX: Slow Compile Time and Execution Time with Query That Contains Aggregates and Subqueries\n" +
        "B	8.0.636		Unknown		2002-06-24		Microsoft Security Bulletin MS02-039\n" +
        "B	8.0.608	Hotfix	Hotfix	319507	2004-06-21		FIX: SQL Extended Procedure Functions Contain Unchecked Buffers\n" +
        "B	8.0.604		Unknown				2000 SP2+3/29 fix\n" +
        "B	8.0.599	Hotfix	Hotfix	319869	2005-09-27		FIX: Improved SQL Manager Robustness for Odd Length Buffer\n" +
        "B	8.0.594	Hotfix	Hotfix	319477	2005-09-27		FIX: Extremely Large Number of User Tables on AWE System May Cause BPool::Map Errors\n" +
        "B	8.0.584	Hotfix	Hotfix	318530	2008-02-04		FIX: Reorder outer joins with filter criteria before non-selective joins and outer joins\n" +
        "B	8.0.578	Hotfix	Hotfix	317979	2005-09-27		FIX: Unchecked Buffer May Occur When You Connect to Remote Data Source\n" +
        "B	8.0.578	Hotfix	Hotfix	318045	2005-09-27		FIX: SELECT with Timestamp Column That Uses FOR XML AUTO May Fail with Stack Overflow or AV\n" +
        "B	8.0.568	Hotfix	Hotfix	317748	2002-10-30		FIX: Handle Leak Occurs in SQL Server When Service or Application Repeatedly Connects and Disconnects with Shared Memory Network Library\n" +
        "B	8.0.561		Unknown				2000 SP2+1/29 fix\n" +
        "B	8.0.558	Hotfix	Hotfix	314003	2005-09-26		FIX: Query That Uses DESC Index May Result in Access Violation\n" +
        "B	8.0.558	Hotfix	Hotfix	315395	2005-09-27		FIX: COM May Not Be Uninitialized for Worker Thread When You Use sp_OA\n" +
        "B	8.0.552		Unknown	313002			The Knowledge Base (KB) Article You Requested Is Currently Not Available\n" +
        "B	8.0.552	Hotfix	Hotfix	313005	2005-09-26		FIX: SELECT from Computed Column That References UDF Causes SQL Server to Terminate\n" +
        "B	8.0.534		Unknown				2000 SP2.01\n" +
        "B	8.0.532	SP2	ServicePack		2003-02-04		SQL Server 2000 Service Pack 2 (SP2)\n" +
        "B	8.0.475		Unknown				2000 SP1+1/29 fix\n" +
        "B	8.0.474	Hotfix	Hotfix	315395	2005-09-27		FIX: COM May Not Be Uninitialized for Worker Thread When You Use sp_OA\n" +
        "B	8.0.473	Hotfix	Hotfix	314003	2005-09-26		FIX: Query That Uses DESC Index May Result in Access Violation\n" +
        "B	8.0.471	Hotfix	Hotfix	313302	2005-09-26		FIX: Shared Table Lock Is Not Released After Lock Escalation\n" +
        "B	8.0.469	Hotfix	Hotfix	313005	2005-09-26		FIX: SELECT from Computed Column That References UDF Causes SQL Server to Terminate\n" +
        "B	8.0.452	Hotfix	Hotfix	308547	2005-09-26		FIX: SELECT DISTINCT from Table with LEFT JOIN of View Causes Error Messages or Client Application May Stop Responding\n" +
        "B	8.0.444	Hotfix	Hotfix	307540	2005-09-26		FIX: SQLPutData May Result in Leak of Buffer Pool Memory\n" +
        "B	8.0.444	Hotfix	Hotfix	307655	2005-10-07		FIX: Querying Syslockinfo with Large Numbers of Locks May Cause Server to Stop Responding\n" +
        "B	8.0.443	Hotfix	Hotfix	307538	2005-09-26		FIX: SQLTrace Start and Stop is Now Reported in Windows NT Event Log for SQL Server 2000\n" +
        "B	8.0.428	Hotfix	Hotfix	304850	2004-08-05		FIX: SQL Server Text Formatting Functions Contain Unchecked Buffers\n" +
        "B	8.0.384	SP1	ServicePack		2001-06-11		SQL Server 2000 Service Pack 1 (SP1)\n" +
        "B	8.0.296	Hotfix	Hotfix	299717	2004-08-09		FIX: Query Method Used to Access Data May Allow Rights that the Login Might Not Normally Have\n" +
        "B	8.0.287	Hotfix	Hotfix	297209	2005-10-07		FIX: Deletes, Updates and Rank Based Selects May Cause Deadlock of MSSEARCH\n" +
        "B	8.0.251	Hotfix	Hotfix	300194	2003-10-17		FIX: Error 644 Using Two Indexes on a Column with Uppercase Preference Sort Order\n" +
        "B	8.0.250		Unknown	291683			The Knowledge Base (KB) Article You Requested Is Currently Not Available\n" +
        "B	8.0.249	Hotfix	Hotfix	288122	2003-09-12		FIX: Lock Monitor Uses Excessive CPU\n" +
        "B	8.0.239	Hotfix	Hotfix	285290	2003-10-09		FIX: Complex ANSI Join Query with Distributed Queries May Cause Handled Access Violation\n" +
        "B	8.0.233	Hotfix	Hotfix	282416	2003-10-09		FIX: Opening the Database Folder in SQL Server Enterprise Manager 2000 Takes a Long Time\n" +
        "B	8.0.231	Hotfix	Hotfix	282279	2003-10-09		FIX: Execution of sp_OACreate on COM Object Without Type Information Causes Server Shut Down\n" +
        "B	8.0.226	Hotfix	Hotfix	278239	2006-11-21		FIX: Extreme Memory Usage When Adding Many Security Roles\n" +
        "B	8.0.225		Unknown	281663	2006-10-30		\"Access Denied\" Error Message When You Try to Use a Network Drive to Modify Windows 2000 Permissions\n" +
        "B	8.0.223	Hotfix	Hotfix	280380	2004-06-29		FIX: Buffer Overflow Exploit Possible with Extended Stored Procedures\n" +
        "B	8.0.222	Hotfix	Hotfix	281769	2005-10-07		FIX: Exception Access Violation Encountered During Query Normalization\n" +
        "B	8.0.218	Hotfix	Hotfix	279183	2003-10-09		FIX: Scripting Object with Several Extended Properties May Cause Exception\n" +
        "B	8.0.217	Hotfix	Hotfix	279293	2003-10-09		FIX: CASE Using LIKE with Empty String Can Result in Access Violation or Abnormal Server Shutdown\n" +
        "B	8.0.211	Hotfix	Hotfix	276329	2003-11-05		FIX: Complex Distinct or Group By Query Can Return Unexpected Results with Parallel Execution Plan\n" +
        "B	8.0.210	Hotfix	Hotfix	275900	2003-10-09		FIX: Linked Server Query with Hyphen in LIKE Clause May Run Slowly\n" +
        "B	8.0.205	Hotfix	Hotfix	274330	2005-10-07		FIX: Sending Open Files as Attachment in SQL Mail Fails with Error 18025\n" +
        "B	8.0.204	Hotfix	Hotfix	274329	2003-10-09		FIX: Optimizer Slow to Generate Query Plan for Complex Queries that have Many Joins and Semi-Joins\n" +
        "B	8.0.194	RTM	Rtm		2000-11-30		SQL Server 2000 RTM (no SP)\n" +
        "B	8.0.190		Unknown				SQL Server 2000 Gold\n" +
        "B	8.0.100		Preview				SQL Server 2000 Beta 2 Beta\n" +
        "B	8.0.078		Unknown				SQL Server 2000 EAP5\n" +
        "B	8.0.047		Unknown				SQL Server 2000 EAP4\n" +
        "R	7.0	SQL Server 7.0	Sphinx	7.0.623	1998-11-27	2005-12-31	2011-01-11\n" +
        "R	6.50	SQL Server 6.5	Hydra	6.50.201	1996-06-30	2002-01-01	\n" +
        "R	6.0	SQL Server 6.0	SQL95	6.0.121	1995-06-13	1999-03-31	\n";
}
