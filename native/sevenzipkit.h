/*
 * sevenzipkit: a small C interface over bit7z, so .NET (or any C caller) can
 * create, extract and test 7z/zip archives with 7-Zip's own engine.
 *
 * All strings are UTF-8. Every call that can fail returns a status and, on
 * failure, leaves a message readable with szk_last_error() until the next
 * call on the same handle. A handle runs one operation at a time; use one
 * handle per worker thread.
 */
#ifndef SEVENZIPKIT_H
#define SEVENZIPKIT_H

#include <stdint.h>

#ifdef _WIN32
#  define SZK_API __declspec(dllexport)
#  define SZK_CALL __cdecl
#else
#  define SZK_API __attribute__((visibility("default")))
#  define SZK_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define SZK_ABI_VERSION 1

typedef enum szk_status {
    SZK_OK = 0,
    SZK_ERROR = 1,     /* see szk_last_error() */
    SZK_CANCELLED = 2, /* the progress callback returned 0 */
    SZK_BAD_ARGS = 3
} szk_status;

typedef enum szk_format {
    SZK_FORMAT_7Z = 0,
    SZK_FORMAT_ZIP = 1
} szk_format;

/* Called as work proceeds; `total` is 0 until the size is known.
 * Return non-zero to continue, 0 to cancel. */
typedef int (SZK_CALL *szk_progress_fn)(void *user, uint64_t done, uint64_t total);

typedef struct szk_compress_options {
    uint32_t struct_size;   /* sizeof(szk_compress_options) */
    int32_t format;         /* szk_format */
    int32_t level;          /* 0 (store), 1, 3, 5, 7, 9 (ultra) */
    uint32_t threads;       /* 0 = 7-Zip's default (all cores) */
    int32_t solid;          /* 7z only: non-zero for a solid archive */
    int32_t contents_only;  /* with one folder input: store its contents, not the folder itself */
} szk_compress_options;

typedef struct sevenzipkit sevenzipkit;

SZK_API int SZK_CALL szk_abi_version(void);

/* Loads 7-Zip's library (7z.dll / 7z.so) from `lib7z_path`. */
SZK_API szk_status SZK_CALL szk_open(const char *lib7z_path, sevenzipkit **out, const char **error);
SZK_API void SZK_CALL szk_close(sevenzipkit *archive);
SZK_API const char *SZK_CALL szk_last_error(const sevenzipkit *archive);

/* Compresses files and folders into `out_path`, which must not exist.
 * Files are stored by name; folders are stored with their contents
 * (or only their contents, see contents_only). */
SZK_API szk_status SZK_CALL szk_compress(sevenzipkit *archive, const szk_compress_options *options,
                                         const char *const *inputs, int32_t input_count, const char *out_path,
                                         szk_progress_fn progress, void *user);

/* Extracts every item of any archive 7-Zip can read into `out_dir`, overwriting existing files. */
SZK_API szk_status SZK_CALL szk_extract(sevenzipkit *archive, const char *archive_path, const char *out_dir,
                                        szk_progress_fn progress, void *user);

/* Decompresses every item and checks its CRC without writing anything. */
SZK_API szk_status SZK_CALL szk_test(sevenzipkit *archive, const char *archive_path,
                                     szk_progress_fn progress, void *user);

/*
 * Raw deflate (RFC 1951: no zlib or gzip wrapper) through 7-Zip's own Deflate
 * encoder, for formats made of independently compressed blocks such as CSO.
 * Added after ABI version 1 without changing it: check that szk_deflater_create
 * exists before using these. A deflater is not thread-safe; use one per thread.
 */
typedef struct szk_deflater szk_deflater;

/* Loads 7-Zip's library from `lib7z_path` and makes a Deflate encoder.
 * `level` is 1 to 9; 9 is 7-Zip's ultra setting (10 passes, 128 fast bytes). */
SZK_API szk_status SZK_CALL szk_deflater_create(const char *lib7z_path, int32_t level,
                                                szk_deflater **out, const char **error);
SZK_API void SZK_CALL szk_deflater_free(szk_deflater *deflater);
SZK_API const char *SZK_CALL szk_deflater_last_error(const szk_deflater *deflater);

/* Compresses `in` into `out`. When the result needs more than `out_capacity`
 * bytes, returns SZK_OK with *out_size set to 0. */
SZK_API szk_status SZK_CALL szk_deflate(szk_deflater *deflater, const void *in, uint32_t in_size,
                                        void *out, uint32_t out_capacity, uint32_t *out_size);

#ifdef __cplusplus
}
#endif

#endif /* SEVENZIPKIT_H */
