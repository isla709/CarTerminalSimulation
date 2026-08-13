#pragma once

#ifdef _WIN32
#define TF_EXPORT __declspec(dllexport)
#else
#define TF_EXPORT
#endif

typedef void (__cdecl *tf_progress_callback)(double progress, void* user_data);

#ifdef __cplusplus
extern "C" {
#endif

TF_EXPORT int __cdecl tf_probe(const wchar_t* input_path, double* frame_rate, int* has_audio);
TF_EXPORT int __cdecl tf_transcode(const wchar_t* input_path, const wchar_t* h264_output_path,
    const wchar_t* g711a_output_path, const wchar_t* aac_output_path, double frame_rate,
    int gop_size, int max_bitrate, int buffer_size, tf_progress_callback callback, void* user_data);
TF_EXPORT int __cdecl tf_thumbnail(const wchar_t* input_path, const wchar_t* output_path);
TF_EXPORT void __cdecl tf_last_error(wchar_t* buffer, int capacity);

#ifdef __cplusplus
}
#endif
