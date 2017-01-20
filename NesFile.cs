﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace com.clusterrr.Famicom
{
    public class NesFile
    {
        public byte[] PRG = null;
        public byte[] CHR = null;
        public byte[] Trainer = null;
        public byte Mapper = 0;
        public MirroringType Mirroring = MirroringType.Horizontal;
        public TvSystemType TvSystem = TvSystemType.Ntsc;
        public bool Battery = false;
        public bool VSunisystem = false;
        public bool PlayChoice10 = false;
        private readonly string fileName = null;

        public enum MirroringType { Horizontal = 0, Vertical = 1, FourScreenVram = 2, Unknown_both = 0xfe, Unknown_none = 0xff };
        public enum TvSystemType { Ntsc = 0, Pal = 1 };

        public enum NesFixType { NoFix = 0, Mapper = 1, Mirroring = 2, Battery = 4, NoChr = 8 };

        public NesFile()
        {
        }

        public NesFile(byte[] data)
        {

            if (data[0] != 0x4E ||
            data[1] != 0x45 ||
            data[2] != 0x53 ||
            data[3] != 0x1A) throw new Exception("Invalid NES file " + fileName);

            if (!(data[12] == 0 && data[13] == 0 && data[14] == 0 && data[15] == 0))
            {
                // archaic iNES
                data[7] = data[8] = data[9] = data[10] = data[11] = data[12] = data[13] = data[14] = data[15] = 0;
            }

            var prgSize = data[4] * 16384;
            var chrSize = data[5] * 8192;
            Mirroring = (MirroringType)(data[6] & 1);
            Battery = (data[6] & (1 << 1)) != 0;
            if ((data[6] & (1 << 2)) != 0)
            {
                Trainer = new byte[512];
            }
            else
            {
                Trainer = null;
            }
            if ((data[6] & (1 << 3)) != 0)
                Mirroring = MirroringType.FourScreenVram;

            Mapper = (byte)((data[6] >> 4) | (data[7] & 0xF0));

            data[7] = 0;

            VSunisystem = (data[7] & 1) != 0;
            PlayChoice10 = (data[7] & (1 << 1)) != 0;

            /*
            header[8] = 0; // PRG RAM size in 8 KB
            header[9] = 0;
             */
            TvSystem = (TvSystemType)(data[9] & 1);

            int offset = 16;
            if (Trainer != null)
            {
                Array.Copy(data, offset, Trainer, 0, 512);
                offset += 512;
            }

            PRG = new byte[prgSize];
            Array.Copy(data, offset, PRG, 0, Math.Max(0, Math.Min(prgSize, data.Length - offset))); // Ignore end of for some bad ROMs
            offset += prgSize;

            CHR = new byte[chrSize];
            Array.Copy(data, offset, CHR, 0, Math.Max(0, Math.Min(chrSize, data.Length - offset)));
        }

        public NesFile(string fileName)
            : this(File.ReadAllBytes(fileName))
        {
            this.fileName = fileName;
        }

        public void Save(string fileName)
        {
            if (PRG == null) PRG = new byte[0];
            if (CHR == null) CHR = new byte[0];
            var data = new List<byte>();
            var header = new byte[16];
            header[0] = 0x4E;
            header[1] = 0x45;
            header[2] = 0x53;
            header[3] = 0x1A;
            header[4] = (byte)(PRG.Length / 16384);
            header[5] = (byte)(CHR.Length / 8192);
            header[6] = 0;
            if (Mirroring == MirroringType.Vertical) header[6] |= 1;
            if (Battery) header[6] |= (1 << 1);
            if (Trainer != null) header[6] |= (1 << 2);
            if (Mirroring == MirroringType.FourScreenVram)
                header[6] |= (1 << 3);
            header[6] |= (byte)(Mapper << 4);

            header[7] = 0;
            if (VSunisystem) header[7] |= 1;
            if (PlayChoice10) header[7] |= 1 << 1;
            header[7] |= (byte)(Mapper & 0xf0);

            header[8] = 0; // PRG RAM size in 8 KB

            header[9] = 0;
            if (TvSystem == TvSystemType.Pal) header[9] |= 1;

            data.AddRange(header);
            data.AddRange(PRG);
            data.AddRange(CHR);
            if (Trainer != null)
                data.AddRange(Trainer);

            //data.AddRange(new byte[127]);
            File.WriteAllBytes(fileName, data.ToArray());
        }

        public byte[] MD5
        {
            get
            {
                MD5 md5 = System.Security.Cryptography.MD5.Create();
                if (PRG == null) PRG = new byte[0];
                if (CHR == null) CHR = new byte[0];
                var alldata = new byte[PRG.Length + CHR.Length];
                Array.Copy(PRG, 0, alldata, 0, PRG.Length);
                Array.Copy(CHR, 0, alldata, PRG.Length, CHR.Length);
                return md5.ComputeHash(alldata);
            }
        }

        public uint CRC32
        {
            get
            {
                if (PRG == null) PRG = new byte[0];
                if (CHR == null) CHR = new byte[0];
                var alldata = new byte[PRG.Length + CHR.Length];
                Array.Copy(PRG, 0, alldata, 0, PRG.Length);
                Array.Copy(CHR, 0, alldata, PRG.Length, CHR.Length);
                uint poly = 0xedb88320;
                uint[] table = new uint[256];
                uint temp = 0;
                for (uint i = 0; i < table.Length; ++i)
                {
                    temp = i;
                    for (int j = 8; j > 0; --j)
                    {
                        if ((temp & 1) == 1)
                        {
                            temp = (uint)((temp >> 1) ^ poly);
                        }
                        else
                        {
                            temp >>= 1;
                        }
                    }
                    table[i] = temp;
                }
                uint crc = 0xffffffff;
                for (int i = 0; i < alldata.Length; ++i)
                {
                    byte index = (byte)(((crc) & 0xff) ^ alldata[i]);
                    crc = (uint)((crc >> 8) ^ table[index]);
                }
                return ~crc;
            }
        }

        public NesFixType CorrectRom()
        {
            int result = 0;
            var crc32 = CRC32;
            for (int i = 0; i < correct.Length; i += 3)
            {
                if (crc32 == correct[i])
                {
                    var mapper = correct[i + 1];
                    var mirroring = correct[i + 2];
                    if (mapper >= 0)
                    {
                        // no CHR
                        if ((mapper & 0x800) != 0 && CHR.Length > 0)
                        {
                            CHR = new byte[0];
                            result |= 8;
                        }
                        // invalid mapper
                        if (Mapper != (mapper & 0xFF))
                        {
                            Mapper = (byte)(mapper & 0xFF);
                            result |= 1;
                        }
                    }
                    if (mirroring >= 0)
                    {
                        // Anything but hard-wired(four screen)
                        if (mirroring == 8 && Mirroring == MirroringType.FourScreenVram)
                        {
                            Mirroring = MirroringType.Horizontal;
                            result |= 2;
                        }
                        MirroringType needMirroring = MirroringType.Unknown_none;
                        switch (mirroring)
                        {
                            case 0:
                                needMirroring = MirroringType.Horizontal;
                                break;
                            case 1:
                                needMirroring = MirroringType.Vertical;
                                break;
                            case 2:
                                needMirroring = MirroringType.FourScreenVram;
                                break;
                        }
                        if (needMirroring != MirroringType.Unknown_none && needMirroring != Mirroring)
                        {
                            Mirroring = needMirroring;
                            result |= 2;
                        }
                    }
                }
            }

            var md5 = MD5;
            ulong partialmd5 = 0;
            for (int x = 0; x < 8; x++)
                partialmd5 |= (ulong)md5[15 - x] << (x * 8);
            // maybe this games uses battery saves?
            foreach (var sav in savie)
            {
                if (!Battery && sav == partialmd5)
                {
                    Battery = true;
                    result |= 4;
                }
            }

            return (NesFixType)result;
        }

        static long[] correct = new long[]
        {
            0xaf5d7aa2,  -1,        0,	/* Clu Clu Land */
	        0xcfb224e6,  -1,        1,	/* Dragon Ninja (J) [p1][!].nes */
	        0x4f2f1846,  -1,        1,	/* Famista '89 - Kaimaku Han!! (J) */
	        0x82f204ae,  -1,        1,	/* Liang Shan Ying Xiong (NJ023) (Ch) [!] */
	        0x684afccd,  -1,        1,	/* Space Hunter (J) */
	        0xad9c63e2,  -1,        1,	/* Space Shadow (J) */
	        0xe1526228,  -1,        1,	/* Quest of Ki */
	        0xaf5d7aa2,  -1,        0,	/* Clu Clu Land */
	        0xcfb224e6,  -1,        1,	/* Dragon Ninja (J) [p1][!].nes */
	        0x4f2f1846,  -1,        1,	/* Famista '89 - Kaimaku Han!! (J) */
	        0xfcdaca80,   0,        0,	/* Elevator Action */
	        0xc05a365b,   0,        0,	/* Exed Exes (J) */
	        0x32fa246f,   0,        0,	/* Tag Team Pro Wrestling */
	        0xb3c30bea,   0,        0,	/* Xevious (J) */
	        0xe492d45a,   0,        0,	/* Zippy Race */
	        0xe28f2596,   0,        1,	/* Pac Land (J) */
	        0xd8ee7669,   1,        8,	/* Adventures of Rad Gravity */
	        0x5b837e8d,   1,        8,	/* Alien Syndrome */
	        0x37ba3261,   1,        8,	/* Back to the Future 2 and 3 */
	        0x5b6ca654,   1,        8,	/* Barbie rev X*/
	        0x61a852ea,   1,        8,	/* Battle Stadium - Senbatsu Pro Yakyuu */
	        0xf6fa4453,   1,        8,	/* Bigfoot */
	        0x391aa1b8,   1,        8,	/* Bloody Warriors (J) */
	        0xa5e8d2cd,   1,        8,	/* Breakthru */
	        0x3f56a392,   1,        8,	/* Captain Ed (J) */
	        0x078ced30,   1,        8,	/* Choujin - Ultra Baseball */
	        0xfe364be5,   1,        8,	/* Deep Dungeon 4 */
	        0x57c12280,   1,        8,	/* Demon Sword */
	        0xd09b74dc,   1,        8,	/* Great Tank (J) */
	        0xe8baa782,   1,        8,	/* Gun Hed (J) */
	        0x970bd9c2,   1,        8,	/* Hanjuku Hero */
	        0xcd7a2fd7,   1,        8,	/* Hanjuku Hero */
	        0x63469396,   1,        8,	/* Hokuto no Ken 4 */
	        0xe94d5181,   1,        8,	/* Mirai Senshi - Lios */
	        0x7156cb4d,   1,        8,	/* Muppet Adventure Carnival thingy */
	        0x70f67ab7,   1,        8,	/* Musashi no Bouken */
	        0x291bcd7d,   1,        8,	/* Pachio Kun 2 */
	        0xa9a4ea4c,   1,        8,	/* Satomi Hakkenden */
	        0xcc3544b0,   1,        8,	/* Triathron */
	        0x934db14a,   1,       -1,	/* All-Pro Basketball */
	        0xf74dfc91,   1,       -1,	/* Win,	Lose,	or Draw */
	        0x9ea1dc76,   2,        0,	/* Rainbow Islands */
	        0x6d65cac6,   2,        0,	/* Terra Cresta */
	        0xe1b260da,   2,        1,	/* Argos no Senshi */
	        0x1d0f4d6b,   2,        1,	/* Black Bass thinging */
	        0x266ce198,   2,        1,	/* City Adventure Touch */
	        0x804f898a,   2,        1,	/* Dragon Unit */
	        0x55773880,   2,        1,	/* Gilligan's Island */
	        0x6e0eb43e,   2,        1,	/* Puss n Boots */
	        0x2bb6a0f8,   2,        1,	/* Sherlock Holmes */
	        0x28c11d24,   2,        1,	/* Sukeban Deka */
	        0x02863604,   2,        1,	/* Sukeban Deka */
	        0x419461d0,   2,        1,	/* Super Cars */
	        0xdbf90772,   3,        0,	/* Alpha Mission */
	        0xd858033d,   3,        0,	/* Armored Scrum Object */
	        0x9bde3267,   3,        1,	/* Adventures of Dino Riki */
	        0xd8eff0df,   3,        1,	/* Gradius (J) */
	        0x1d41cc8c,   3,        1,	/* Gyruss */
	        0xcf322bb3,   3,        1,	/* John Elway's Quarterback */
	        0xb5d28ea2,   3,        1,	/* Mystery Quest - mapper 3?*/
	        0x02cc3973,   3,        1,	/* Ninja Kid */
	        0xbc065fc3,   3,        1,	/* Pipe Dream */
	        0xc9ee15a7,   3,       -1,	/* 3 is probably best.  41 WILL NOT WORK. */
	        0x13e09d7a,   4,        0, /*Dragon Wars (U) (proto) - comes with erroneous 4-screen mirroring set*/
	        0x22d6d5bd,   4,        1,
            0xd97c31b0,   4,        1,	//Rasaaru Ishii no Childs Quest (J)
	        0x404b2e8b,   4,        2,	/* Rad Racer 2 */
	        0x15141401,   4,        8,	/* Asmik Kun Land */
	        0x4cccd878,   4,        8,	/* Cat Ninden Teyandee */
	        0x59280bec,   4,        8,	/* Jackie Chan */
	        0x7474ac92,   4,        8,	/* Kabuki: Quantum Fighter */
	        0x5337f73c,   4,        8,	/* Niji no Silk Road */
	        0x9eefb4b4,   4,        8,	/* Pachi Slot Adventure 2 */
	        0x21a653c7,   4,       -1,	/* Super Sky Kid */
	        0x9cbadc25,   5,        8,	/* JustBreed */
	        0xf518dd58,   7,        8,	/* Captain Skyhawk */
	        0x84382231,   9,        0,	/* Punch Out (J) */
	        0xbe939fce,   9,        1,	/* Punchout*/
	        0x345d3a1a,  11,        1,	/* Castle of Deceit */
	        0x5e66eaea,  13,        1,	/* Videomation */
	        0xcd373baa,  14,       -1,	/* Samurai Spirits (Rex Soft) */
	        0xbfc7a2e9,  16,        8,
            0x6e68e31a,  16,        8,	/* Dragon Ball 3*/
	        0x33b899c9,  16,       -1,	/* Dragon Ball - Dai Maou Fukkatsu (J) [!] */
	        0xa262a81f,  16,       -1,	/* Rokudenashi Blues (J) */
	        0x286fcd20,  23,       -1,	/* Ganbare Goemon Gaiden 2 - Tenka no Zaihou (J) [!] */
	        0xe4a291ce,  23,       -1,	/* World Hero (Unl) [!] */
	        0x51e9cd33,  23,       -1,	/* World Hero (Unl) [b1] */
	        0x105dd586,  27,       -1,	/* Mi Hun Che variations... */
	        0xbc9bb6c1,  27,       -1,	/* -- */
	        0x43753886,  27,       -1,	/* -- */
	        0x5b3de3d1,  27,       -1,	/* -- */
	        0x511e73f8,  27,       -1,	/* -- */
	        0x5555fca3,  32,        8,
            0x283ad224,  32,        8,	/* Ai Sensei no Oshiete */
	        0x243a8735,  32,   0x10|4,	/* Major League */
	        0xbc7b1d0f,  33,       -1, /* Bakushou!! Jinsei Gekijou 2 (J) [!] */
            //0xc2730c30,	 34,		0,	/* Deadly Towers */ // Duplicate value? WTF?
	        0x4c7c1af3,  34,        1,	/* Caesar's Palace */
	        0x932ff06e,  34,        1,	/* Classic Concentration */
	        0xf46ef39a,  37,       -1,	/* Super Mario Bros. + Tetris + Nintendo World Cup (E) [!] */
	        0x7ccb12a3,  43,       -1,	/* SMB2j */
	        0x6c71feae,  45,       -1,	/* Kunio 8-in-1 */
	        0xe2c94bc2,  48,       -1,	/* Super Bros 8 (Unl) [!] */
	        0xaebd6549,  48,        8,	/* Bakushou!! Jinsei Gekijou 3 */
	        0x6cdc0cd9,  48,        8,	/* Bubble Bobble 2 */
	        0x99c395f9,  48,        8,	/* Captain Saver */
	        0xa7b0536c,  48,        8,	/* Don Doko Don 2 */
	        0x40c0ad47,  48,        8,	/* Flintstones 2 */
	        0x1500e835,  48,        8,	/* Jetsons (J) */
	        0xa912b064,  51|0x800,  8,	/* 11-in-1 Ball Games (has CHR ROM when it shouldn't) */
	        0xb19a55dd,  64,        8,	/* Road Runner */
	        0xf92be3ec,  64,       -1,	/* Rolling Thunder */
	        0xe84274c5,  66,        1,
            0xbde3ae9b,  66,        1,	/* Doraemon */
	        0x9552e8df,  66,        1,	/* Dragon Ball */
	        0x811f06d9,  66,        1,	/* Dragon Power */
	        0xd26efd78,  66,        1,	/* SMB Duck Hunt */
	        0xdd8ed0f7,  70,        1,	/* Kamen Rider Club */
	        0xbba58be5,  70,       -1,	/* Family Trainer - Manhattan Police */
	        0x370ceb65,  70,       -1,	/* Family Trainer - Meiro Dai Sakusen */
	        0xe62e3382,  71,       -1,	/* Mig-29 Soviet Fighter */
	        0xac7b0742,  71,       -1,	/* Golden KTV (Ch) [!], not actually 71, but UNROM without BUS conflict */
	        0x054bd3e9,  74,       -1,	/* Di 4 Ci - Ji Qi Ren Dai Zhan (As) */
	        0x496ac8f7,  74,       -1,	/* Ji Jia Zhan Shi (As) */
	        0xae854cef,  74,       -1,	/* Jia A Fung Yun (Chinese) */
	        0xba51ac6f,  78,        2,
            0x3d1c3137,  78,        8,	/* Uchuusen - Cosmo Carrier */
	        0xa4fbb438,  79,        0,
            0xd4a76b07,  79,        0,	/* F-15 City Wars*/
	        0x1eb4a920,  79,        1,	/* Double Strike */
	        0x3e1271d5,  79,        1,	/* Tiles of Fate */
	        0xd2699893,  88,        0,	/*  Dragon Spirit */
	        0xbb7c5f7a,  89,        8,	/* Mito Koumon or something similar */
	        0x0da5e32e, 101,       -1,	/* new Uruusey Yatsura */
	        0x8eab381c, 113,        1,	/* Death Bots */
	        0x6a03d3f3, 114,       -1,
            0x0d98db53, 114,       -1,	/* Pocahontas */
	        0x4e7729ff, 114,       -1,	/* Super Donkey Kong */
	        0xc5e5c5b2, 115,       -1,	/* Bao Qing Tian (As).nes */
	        0xa1dc16c0, 116,       -1,
            0xe40dfb7e, 116,       -1,	/* Somari (P conf.) */
	        0xc9371ebb, 116,       -1,	/* Somari (W conf.) */
	        0xcbf4366f, 118,        8,	/* Alien Syndrome (U.S. unlicensed) */
	        0x78b657ac, 118,       -1,	/* Armadillo */
	        0x90c773c1, 118,       -1,	/* Goal! 2 */
	        0xb9b4d9e0, 118,       -1,	/* NES Play Action Football */
	        0x07d92c31, 118,       -1,	/* RPG Jinsei Game */
	        0x37b62d04, 118,       -1,	/* Ys 3 */
	        0x318e5502, 121,       -1,	/* Sonic 3D Blast 6 (Unl) */
	        0xddcfb058, 121,       -1,	/* Street Fighter Zero 2 '97 (Unl) [!] */
	        0x5aefbc94, 133,       -1,	/* Jovial Race (Sachen) [a1][!] */
	        0xc2df0a00, 140,        1,	/* Bio Senshi Dan(hacked) */
	        0xe46b1c5d, 140,        1,	/* Mississippi Satsujin Jiken */
	        0x3293afea, 140,        1,	/* Mississippi Satsujin Jiken */
	        0x6bc65d7e, 140,        1,	/* Youkai Club*/
	        0x5caa3e61, 144,        1,	/* Death Race */
	        0x48239b42, 146,       -1,	/* Mahjong Companion (Sachen) [!] */
	        0xb6a727fa, 146,       -1,	/* Papillion (As) [!] */
	        0xa62b79e1, 146,       -1,	/* Side Winder (HES) [!] */
	        0xcc868d4e, 149,       -1,	/* 16 Mahjong [p1][!] */
	        0x29582ca1, 150,       -1,
            0x40dbf7a2, 150,       -1,
            0x73fb55ac, 150,       -1,	/* 2-in-1 Cosmo Cop + Cyber Monster (Sachen) [!] */
	        0xddcbda16, 150,       -1,	/* 2-in-1 Tough Cop + Super Tough Cop (Sachen) [!] */
	        0x47918d84, 150,       -1,	/* auto-upturn */
	        0x0f141525, 152,        8,	/* Arkanoid 2 (Japanese) */
	        0xbda8f8e4, 152,        8,	/* Gegege no Kitarou 2 */
	        0xb1a94b82, 152,        8,	/* Pocket Zaurus */
	        0x026c5fca, 152,        8,	/* Saint Seiya Ougon Densetsu */
	        0x3f15d20d, 153,        8,	/* Famicom Jump 2 */
	        0xd1691028, 154,        8,	/* Devil Man */
	        0xcfd4a281, 155,        8,	/* Money Game.  Yay for money! */
	        0x2f27cdef, 155,        8,	/* Tatakae!! Rahmen Man */
	        0xccc03440, 156,       -1,
            0x983d8175, 157,        8,	/* Datach Battle Rush */
	        0x894efdbc, 157,        8,	/* Datach Crayon Shin Chan */
	        0x19e81461, 157,        8,	/* Datach DBZ */
	        0xbe06853f, 157,        8,	/* Datach J-League */
	        0x0be0a328, 157,        8,	/* Datach SD Gundam Wars */
	        0x5b457641, 157,        8,	/* Datach Ultraman Club */
	        0xf51a7f46, 157,        8,	/* Datach Yuu Yuu Hakusho */
	        0xe170404c, 159,       -1,	/* SD Gundam Gaiden - Knight Gundam Monogatari (J) (V1.0) [!] */
	        0x276ac722, 159,       -1,	/* SD Gundam Gaiden - Knight Gundam Monogatari (J) (V1.1) [!] */
	        0x0cf42e69, 159,       -1,	/* Magical Taruruuto-kun - Fantastic World!! (J) (V1.0) [!] */
	        0xdcb972ce, 159,       -1,	/* Magical Taruruuto-kun - Fantastic World!! (J) (V1.1) [!] */
	        0xb7f28915, 159,       -1,	/* Magical Taruruuto-kun 2 - Mahou Daibouken (J) */
	        0x183859d2, 159,       -1,	/* Dragon Ball Z - Kyoushuu! Saiya Jin (J) [!] */
	        0x58152b42, 160,        1,	/* Pipe 5 (Sachen) */
	        0x1c098942, 162,       -1,	/* Xi You Ji Hou Zhuan (Ch) */
	        0x081caaff, 163,       -1,	/* Commandos (Ch) */
	        0x02c41438, 176,       -1,	/* Xing He Zhan Shi (C) */
	        0x558c0dc3, 178,       -1,	/* Super 2in1 (unl)[!] mapper unsupported */
	        0xc68363f6, 180,        0,	/* Crazy Climber */
	        0x0f05ff0a, 181,       -1,	/* Seicross  (redump) */
	        0x96ce586e, 189,        8,	/* Street Fighter 2 YOKO */
	        0x555a555e, 191,       -1,
            0x2cc381f6, 191,       -1,	/* Sugoro Quest - Dice no Senshitachi (As) */
	        0xa145fae6, 192,       -1,
            0xa9115bc1, 192,       -1,
            0x4c7bbb0e, 192,       -1,
            0x98c1cd4b, 192,       -1,	/* Ying Lie Qun Xia Zhuan (Chinese) */
	        0xee810d55, 192,       -1,	/* You Ling Xing Dong (Ch) */
	        0x442f1a29, 192,       -1,	/* Young chivalry */
	        0x637134e8, 193,        1,	/* Fighting Hero */
	        0xa925226c, 194,       -1,	/* Dai-2-Ji - Super Robot Taisen (As) */
	        0x7f3dbf1b, 195,        0,
            0xb616885c, 195,        0,	/* CHaos WOrld (Ch)*/
	        0x33c5df92, 195,       -1,
            0x1bc0be6c, 195,       -1,	/* Captain Tsubasa Vol 2 - Super Striker (C) */
	        0xd5224fde, 195,       -1,	/* Crystalis (c) */
	        0xfdec419f, 196,       -1,	/* Street Fighter VI 16 Peoples (Unl) [!] */
	        0x700705f4, 198,       -1,
            0x9a2cf02c, 198,       -1,
            0xd8b401a7, 198,       -1,
            0x28192599, 198,       -1,
            0x19b9e732, 198,       -1,
            0xdd431ba7, 198,       -1,	/* Tenchi wo kurau 2 (c) */
	        0xd871d3e6, 199,       -1,	/* Dragon Ball Z 2 - Gekishin Freeza! (C) */
	        0xed481b7c, 199,       -1,	/* Dragon Ball Z Gaiden - Saiya Jin Zetsumetsu Keikaku (C) */
	        0x44c20420, 199,       -1,	/* San Guo Zhi 2 (C) */
	        0x4e1c1e3c, 206,        0,	/* Karnov */
	        0x276237b3, 206,        0,	/* Karnov */
	        0x7678f1d5, 207,        8,	/* Fudou Myouou Den */
	        0x07eb2c12, 208,       -1,	/* Street Fighter IV */
	        0xdd8ced31, 209,       -1,	/* Power Rangers 3 */
	        0x063b1151, 209,       -1,	/* Power Rangers 4 */
	        0xdd4d9a62, 209,       -1,	/* Shin Samurai Spirits 2 */
	        0x0c47946d, 210,        1,	/* Chibi Maruko Chan */
	        0xc247cc80, 210,        1,	/* Family Circuit '91 */
	        0x6ec51de5, 210,        1,	/* Famista '92 */
	        0xadffd64f, 210,        1,	/* Famista '93 */
	        0x429103c9, 210,        1,	/* Famista '94 */
	        0x81b7f1a8, 210,        1,	/* Heisei Tensai Bakabon */
	        0x2447e03b, 210,        1,	/* Top Striker */
	        0x1dc0f740, 210,        1,	/* Wagyan Land 2 */
	        0xd323b806, 210,        1,	/* Wagyan Land 3 */
	        0xbd523011, 210,        0,	/* Dream Master */
	        0x5daae69a, 211,       -1,	/* Aladdin - Return of Jaffar, The (Unl) [!] */
	        0x1ec1dfeb, 217,       -1,	/* 255-in-1 (Cut version) [p1] */
	        0x046d70cc, 217,       -1,	/* 500-in-1 (Anim Splash, Alt Mapper)[p1][!] */
	        0x12f86a4d, 217,       -1,	/* 500-in-1 (Static Splash, Alt Mapper)[p1][!] */
	        0xd09f778d, 217,       -1,	/* 9999999-in-1 (Static Splash, Alt Mapper)[p1][!] */
	        0x62ef6c79, 232,        8,	/* Quattro Sports -Aladdin */
	        0x2705eaeb, 234,       -1,	/* Maxi 15 */
	        0x6f12afc5, 235,       -1,	/* Golden Game 150-in-1 */
	        0xfb2b6b10, 241,       -1,	/* Fan Kong Jing Ying (Ch) */
	        0xb5e83c9a, 241,       -1,	/* Xing Ji Zheng Ba (Ch) */
	        0x2537b3e6, 241,       -1,	/* Dance Xtreme - Prima (Unl) */
	        0x11611e89, 241,       -1,	/* Darkseed (Unl) [p1] */
	        0x81a37827, 241,       -1,	/* Darkseed (Unl) [p1][b1] */
            0xc2730c30, 241,       -1,	/* Deadly Towers (U) [!] */ // duplicate value, WTF? This one is better
	        0x368c19a8, 241,       -1,	/* LIKO Study Cartridge 3-in-1 (Unl) [!] */
	        0xa21e675c, 241,       -1,	/* Mashou (J) [!] */
	        0x54d98b79, 241,       -1,	/* Titanic 1912 (Unl) */
	        0x6bea1235, 245,       -1,	/* MMC3 cart, but with nobanking applied to CHR-RAM, so let it be there */
	        0x345ee51a, 245,       -1,	/* DQ4c */
	        0x57514c6c, 245,       -1,	/* Yong Zhe Dou E Long - Dragon Quest VI (Ch) */		        
            0xba51ac6f, 78,         8,  /* Holy Diver */
            0x0ae6c9e2, 2,          1,  /* Castelian */
            0x1b71ccdb, 4,          8,  /* Gauntlet II */
            0x59977a46, 0,          1,  /* Mach Rider */
            0x79f80127, 0,          1,  /* Mach Rider (rus) */
        };
        static ulong[] savie = new ulong[]
        {
            0xc04361e499748382,	/* AD&D Heroes of the Lance */
		    0xb72ee2337ced5792,	/* AD&D Hillsfar */
		    0x2b7103b7a27bd72f,	/* AD&D Pool of Radiance */
		    0x498c10dc463cfe95,	/* Battle Fleet */
		    0x854d7947a3177f57,	/* Crystalis */
		    0x4a1f5336b86851b6,	/* DW */
		    0xb0bcc02c843c1b79,	/* DW */
		    0x2dcf3a98c7937c22,	/* DW 2 */
		    0x98e55e09dfcc7533,	/* DW 4*/
		    0x733026b6b72f2470,	/* Dw 3 */
		    0x6917ffcaca2d8466,	/* Famista '90 */
		    0x8da46db592a1fcf4,	/* Faria */
		    0xedba17a2c4608d20,	/* Final Fantasy */
		    0x91a6846d3202e3d6,	/* Final Fantasy */
		    0x012df596e2b31174,	/* Final Fantasy 1+2 */
		    0xf6b359a720549ecd,	/* Final Fantasy 2 */
		    0x5a30da1d9b4af35d,	/* Final Fantasy 3 */
		    0xd63dcc68c2b20adc,	/* Final Fantasy J */
		    0x2ee3417ba8b69706,	/* Hydlide 3*/
		    0xebbce5a54cf3ecc0,	/* Justbreed */
		    0x6a858da551ba239e,	/* Kaijuu Monogatari */
		    0x2db8f5d16c10b925,	/* Kyonshiizu 2 */
		    0x04a31647de80fdab,	/* Legend of Zelda */
		    0x94b9484862a26cba,	/* Legend of Zelda */
		    0xa40666740b7d22fe,	/* Mindseeker */
		    0x82000965f04a71bb,	/* Mirai Shinwa Jarvas */
		    0x77b811b2760104b9,	/* Mouryou Senki Madara */
		    0x11b69122efe86e8c,	/* RPG Jinsei Game */
		    0x9aa1dc16c05e7de5,	/* Startropics */
		    0x1b084107d0878bd0,	/* Startropics 2*/
		    0xa70b495314f4d075,	/* Ys 3 */
		    0x836c0ff4f3e06e45,	/* Zelda 2 */
	    };
    }
}
