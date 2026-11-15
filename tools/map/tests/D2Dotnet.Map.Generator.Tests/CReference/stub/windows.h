/*
 * Minimal <windows.h> stand-in so the upstream map generator sources compile with a 32-bit
 * Linux g++ (g++ -m32) for the golden-output reference build. Only what the sources use.
 */
#pragma once
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef unsigned int DWORD;
typedef unsigned short WORD;
typedef unsigned char BYTE;
typedef int BOOL;
typedef char CHAR;
typedef long LONG;
#define VOID void
typedef void *HKEY;
typedef void *HMODULE;
typedef void *HWND;
typedef const char *LPCTSTR;
typedef const char *LPCSTR;
typedef BYTE *LPBYTE;

#define TRUE 1
#define FALSE 0
#define MAX_PATH 260
#define __stdcall
#define __fastcall
#define TEXT(x) x

#define HKEY_CURRENT_USER ((HKEY)(uintptr_t)0x80000001)
#define KEY_ALL_ACCESS 0xF003F
#define ERROR_SUCCESS 0
#define REG_SZ 1

LONG RegOpenKeyEx(HKEY hKey, LPCTSTR subKey, DWORD options, DWORD sam, HKEY *result);
LONG RegSetValueEx(HKEY hKey, LPCTSTR name, DWORD reserved, DWORD type, const BYTE *data, DWORD size);
LONG RegCloseKey(HKEY hKey);
BOOL SetCurrentDirectory(LPCTSTR path);
void ExitProcess(unsigned int code);

#define sprintf_s(buf, size, ...) snprintf(buf, size, __VA_ARGS__)
