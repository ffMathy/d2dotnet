/* Deterministic clock for the reference build: log.c's gettimeofday is renamed to this (-D). */
#include <sys/time.h>

int fake_gettimeofday(struct timeval *tv, void *tz) {
    (void)tz;
    tv->tv_sec = 1;
    tv->tv_usec = 0;
    return 0;
}
