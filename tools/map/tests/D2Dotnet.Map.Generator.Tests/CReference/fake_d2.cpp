/*
 * Reference harness: replaces upstream offset.c (DefineOffsets) and the Win32 calls with a fake
 * Diablo 2 that builds exactly the same world as TestWorld in ProgramTests.cs. Linked with the
 * *unmodified* upstream json.c, log.c, map.c, main.c and d2_client.c (only the two inline-asm
 * magic jump bodies are stripped, since they jump into D2Client.dll), it produces the golden
 * outputs in ../Golden that GoldenOutputTests compares the C# port against byte for byte.
 */
#define _DEFINE_VARS
#include <stdio.h>
#include <unistd.h>
#include "d2_ptrs.h"

/* ---- Win32 stubs ---- */
LONG RegOpenKeyEx(HKEY, LPCTSTR, DWORD, DWORD, HKEY *result) { *result = (HKEY)0x1234; return getenv("FAKE_NO_REGISTRY") ? 2 : ERROR_SUCCESS; }
LONG RegSetValueEx(HKEY, LPCTSTR, DWORD, DWORD, const BYTE *, DWORD) { return 0; }
LONG RegCloseKey(HKEY) { return 0; }
BOOL SetCurrentDirectory(LPCTSTR) { return 1; }
void ExitProcess(unsigned int code) { _exit(code); } /* does not flush stdio */

/* ---- world (mirrors TestWorld) ---- */
static LevelTxt txt1, txt2;
static ObjectTxt otxt2, otxt3;
static ActMisc misc;
static Act act;
static Level level1, level2;
static Room1 room1A, room1B;
static CollMap collA, collB;
static Room2 roomA, roomB, exitRoom;
static RoomTile tile1, tile2;
static DWORD num9 = 9, num5 = 5;
static PresetUnit pa[6], pb[2];
static WORD cellsA[25] = {0, 0, 1024, 1024, 0, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1};
static WORD cellsB[25] = {0, 0, 0, 0, 0, 1024, 1024, 1024, 1024, 1024, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 5, 5, 5, 5, 5};
static DWORD stormPod = 0xDEADBEEF, stormPd2 = 0xDEADBEEF;

static void preset(PresetUnit *p, DWORD type, DWORD txt, DWORD x, DWORD y, PresetUnit *next) {
    p->dwType = type; p->dwTxtFileNo = txt; p->dwPosX = x; p->dwPosY = y; p->pPresetNext = next;
}

static void build_world() {
    txt1.dwLevelNo = 1; strcpy(txt1.szName, "Rogue Encampment");
    txt2.dwLevelNo = 2; strcpy(txt2.szName, "Blood Moor");
    strcpy(otxt2.szName, "Shrine"); otxt2.nSelectable0 = 1; otxt2.nOperateFn = 2;
    strcpy(otxt3.szName, "casket6"); otxt3.nSelectable0 = 0; otxt3.nOperateFn = 1;

    act.pMisc = &misc;
    level2.dwLevelNo = 2; level2.dwPosX = 12; level2.dwPosY = 20; level2.dwSizeX = 5; level2.dwSizeY = 5; level2.pMisc = &misc;
    level1.dwLevelNo = 1; level1.dwPosX = 10; level1.dwPosY = 20; level1.dwSizeX = 2; level1.dwSizeY = 1; level1.pMisc = &misc; level1.pNextLevel = &level2;
    misc.pLevelFirst = &level1; misc.pAct = &act;

    collA.dwPosGameX = 50; collA.dwPosGameY = 100; collA.dwSizeGameX = 5; collA.dwSizeGameY = 5; collA.pMapStart = cellsA; collA.pMapEnd = cellsA + 25;
    collB.dwPosGameX = 55; collB.dwPosGameY = 100; collB.dwSizeGameX = 5; collB.dwSizeGameY = 5; collB.pMapStart = cellsB; collB.pMapEnd = cellsB + 25;
    room1A.Coll = &collA;
    room1B.Coll = &collB;

    exitRoom.pLevel = &level2; exitRoom.dwPosX = 60; exitRoom.dwPosY = 20;
    tile2.nNum = &num5; tile2.pRoom2 = &exitRoom;
    tile1.nNum = &num9; tile1.pRoom2 = &exitRoom; tile1.pNext = &tile2;

    preset(&pb[1], 2, 3, 1, 2, NULL);
    preset(&pb[0], 2, 7, 0, 0, &pb[1]);
    roomB.dwPosX = 11; roomB.dwPosY = 20; roomB.pRoom1 = &room1B; roomB.pLevel = &level1; roomB.pPreset = &pb[0];

    preset(&pa[5], 4, 1, 0, 0, NULL);
    preset(&pa[4], 5, 5, 4, 0, &pa[5]);
    preset(&pa[3], 2, 580, 2, 2, &pa[4]);
    preset(&pa[2], 2, 2, 1, 1, &pa[3]);
    preset(&pa[1], 1, 149, 0, 0, &pa[2]);
    preset(&pa[0], 1, 19, 3, 4, &pa[1]);
    roomA.dwPosX = 10; roomA.dwPosY = 20; roomA.pRoom2Next = &roomB; roomA.pLevel = &level1; roomA.pPreset = &pa[0]; roomA.pRoomTiles = &tile1;
}

/* ---- fake exports ---- */
static VOID f_fog_10021(CHAR *) {}
static DWORD f_ret1_4(CHAR *, DWORD, CHAR *, DWORD) { return 1; }
static DWORD f_ret1_2(DWORD, DWORD) { return 1; }
static DWORD f_ret1_1(DWORD) { return 1; }
static DWORD f_ret1_0(VOID) { return 1; }
static DWORD f_ret1_3(DWORD, DWORD, DWORD) { return 1; }
static DWORD f_lang(DWORD, CHAR *, DWORD) { return 1; }
static DWORD f_win(DWORD, DWORD, DWORD, d2client_struct *) { return 1; }
static VOID f_misc(DWORD, DWORD, DWORD) {}
static Act *f_load_act(DWORD actNumber, DWORD, DWORD, DWORD, DWORD, DWORD, DWORD, DWORD, DWORD) { return actNumber == 0 ? &act : NULL; }
static VOID f_unload_act(Act *) {}
static LevelTxt *f_level_txt(DWORD n) { return n == 1 ? &txt1 : n == 2 ? &txt2 : NULL; }
static ObjectTxt *f_object_txt(DWORD n) { return n == 2 ? &otxt2 : n == 3 ? &otxt3 : NULL; }
static void f_init_level(Level *p) { if (p == &level1) level1.pRoom2First = &roomA; }
static void f_add_room(Act *, int, int x, int y, Room1 *) {
    if (roomA.dwPosX == (DWORD)x && roomA.dwPosY == (DWORD)y && roomA.pRoom1 == NULL) roomA.pRoom1 = &room1A;
}
static Level *f_get_level(ActMisc *, DWORD) { return NULL; }

void DefineOffsets() {
    build_world();
    FOG_10021 = f_fog_10021;
    D2COMMON_LoadAct = f_load_act;
    FOG_10019 = f_ret1_4;
    FOG_10101 = f_ret1_2;
    FOG_10089 = f_ret1_1;
    FOG_10218 = f_ret1_0;
    D2CLIENT_Pod_InitGameMisc_I = f_misc;
    p_STORM_Pod_MPQHashTable = &stormPod;
    D2CLIENT_Pod_LoadAct_1 = 0x6FAB0000 + 0x737F0;
    D2CLIENT_Pod_LoadAct_2 = 0x6FAB0000 + 0x2B420;
    D2COMMON_Pod_AddRoomData = f_add_room;
    D2COMMON_Pod_RemoveRoomData = f_add_room;
    D2COMMON_Pod_GetLevel = f_get_level;
    D2COMMON_Pod_InitDataTables = f_ret1_3;
    D2COMMON_Pod_InitLevel = f_init_level;
    D2COMMON_Pod_LoadAct = f_load_act;
    D2COMMON_Pod_UnloadAct = f_unload_act;
    D2COMMON_Pod_GetLevelText = f_level_txt;
    D2COMMON_Pod_GetObjectTxt = f_object_txt;
    D2COMMON_10081 = f_ret1_3;
    D2LANG_10009 = f_lang;
    D2WIN_10174 = f_ret1_0;
    D2WIN_10072 = f_win;
    D2CLIENT_Pd2_InitGameMisc_I = f_misc;
    p_STORM_Pd2_MPQHashTable = &stormPd2;
    D2CLIENT_Pd2_LoadAct_1 = 0x6FAB0000 + 0x62AA0;
    D2CLIENT_Pd2_LoadAct_2 = 0x6FAB0000 + 0x62760;
    D2COMMON_Pd2_AddRoomData = f_add_room;
    D2COMMON_Pd2_RemoveRoomData = f_add_room;
    D2COMMON_Pd2_GetLevel = f_get_level;
    D2COMMON_Pd2_InitLevel = f_init_level;
    D2COMMON_Pd2_LoadAct = f_load_act;
    D2COMMON_Pd2_UnloadAct = f_unload_act;
    D2COMMON_Pd2_GetLevelText = f_level_txt;
    D2COMMON_Pd2_GetObjectTxt = f_object_txt;
    D2COMMON_Pd2_InitDataTables = f_ret1_3;
    D2WIN_10086 = f_ret1_0;
    D2WIN_10005 = f_win;
    D2LANG_10008 = f_lang;
    D2COMMON_10943 = f_ret1_3;
}
