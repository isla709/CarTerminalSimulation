#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/avutil.h>
#include <libavutil/audio_fifo.h>
#include <libavutil/channel_layout.h>
#include <libavutil/imgutils.h>
#include <libavutil/opt.h>
#include <libswscale/swscale.h>
#include <libswresample/swresample.h>
#include "terminal_ffmpeg.h"

static _Thread_local wchar_t last_error[1024];

static char* utf8(const wchar_t* value) {
    if (!value) return NULL;
    int size = WideCharToMultiByte(CP_UTF8, 0, value, -1, NULL, 0, NULL, NULL);
    char* result = (char*)av_malloc((size_t)size);
    if (result) WideCharToMultiByte(CP_UTF8, 0, value, -1, result, size, NULL, NULL);
    return result;
}

static int fail(int code, const wchar_t* operation) {
    char detail[AV_ERROR_MAX_STRING_SIZE] = {0};
    av_strerror(code, detail, sizeof(detail));
    wchar_t wide[768] = {0};
    MultiByteToWideChar(CP_UTF8, 0, detail, -1, wide, 768);
    _snwprintf_s(last_error, 1024, _TRUNCATE, L"%s: %s (%d)", operation, wide, code);
    return code < 0 ? code : AVERROR_UNKNOWN;
}

static int open_decoder(AVFormatContext* input, int index, AVCodecContext** context) {
    const AVCodec* codec = avcodec_find_decoder(input->streams[index]->codecpar->codec_id);
    if (!codec) return AVERROR_DECODER_NOT_FOUND;
    *context = avcodec_alloc_context3(codec);
    if (!*context) return AVERROR(ENOMEM);
    int rc = avcodec_parameters_to_context(*context, input->streams[index]->codecpar);
    if (rc >= 0) rc = avcodec_open2(*context, codec, NULL);
    return rc;
}

TF_EXPORT int __cdecl tf_probe(const wchar_t* input_path, double* frame_rate, int* has_audio) {
    AVFormatContext* input = NULL;
    char* path = utf8(input_path);
    if (!path) return fail(AVERROR(ENOMEM), L"路径转换");
    int rc = avformat_open_input(&input, path, NULL, NULL);
    if (rc >= 0) rc = avformat_find_stream_info(input, NULL);
    if (rc >= 0) {
        int video = av_find_best_stream(input, AVMEDIA_TYPE_VIDEO, -1, -1, NULL, 0);
        if (video < 0) rc = video;
        else {
            AVRational rate = av_guess_frame_rate(input, input->streams[video], NULL);
            *frame_rate = rate.den ? av_q2d(rate) : 25.0;
            *has_audio = av_find_best_stream(input, AVMEDIA_TYPE_AUDIO, -1, -1, NULL, 0) >= 0;
        }
    }
    avformat_close_input(&input);
    av_free(path);
    return rc < 0 ? fail(rc, L"媒体探测") : 0;
}

typedef struct VideoOutput {
    AVCodecContext* encoder;
    FILE* file;
    struct SwsContext* scaler;
    AVFrame* frame;
    int64_t pts;
} VideoOutput;

static int open_video_output(VideoOutput* output, AVCodecContext* decoder, const char* path,
    double fps, int gop, int max_rate, int buffer_size) {
    memset(output, 0, sizeof(*output));
    const AVCodec* codec = avcodec_find_encoder_by_name("libx264");
    if (!codec) return AVERROR_ENCODER_NOT_FOUND;
    output->encoder = avcodec_alloc_context3(codec);
    if (!output->encoder) return AVERROR(ENOMEM);
    output->encoder->width = decoder->width;
    output->encoder->height = decoder->height;
    output->encoder->pix_fmt = AV_PIX_FMT_YUV420P;
    output->encoder->time_base = av_d2q(1.0 / fps, 100000);
    output->encoder->framerate = av_d2q(fps, 100000);
    output->encoder->gop_size = gop;
    output->encoder->max_b_frames = 0;
    output->encoder->bit_rate = max_rate;
    output->encoder->rc_max_rate = max_rate;
    output->encoder->rc_buffer_size = buffer_size;
    av_opt_set(output->encoder->priv_data, "preset", "veryfast", 0);
    av_opt_set(output->encoder->priv_data, "crf", "26", 0);
    int rc = avcodec_open2(output->encoder, codec, NULL);
    if (rc < 0) return rc;
    output->file = fopen(path, "wb");
    if (!output->file) return AVERROR(errno);
    output->scaler = sws_getContext(decoder->width, decoder->height, decoder->pix_fmt,
        decoder->width, decoder->height, AV_PIX_FMT_YUV420P, SWS_BILINEAR, NULL, NULL, NULL);
    output->frame = av_frame_alloc();
    if (!output->scaler || !output->frame) return AVERROR(ENOMEM);
    output->frame->format = AV_PIX_FMT_YUV420P;
    output->frame->width = decoder->width;
    output->frame->height = decoder->height;
    return av_frame_get_buffer(output->frame, 32);
}

static void write_adts_header(uint8_t* header, int payload_size) {
    int length = payload_size + 7;
    header[0] = 0xFF;
    header[1] = 0xF1;
    header[2] = 0x6C; /* AAC LC, 8000 Hz, channel config high bit 0 */
    header[3] = (uint8_t)(0x40 | ((length >> 11) & 0x03)); /* channel config 1 (mono) */
    header[4] = (uint8_t)((length >> 3) & 0xFF);
    header[5] = (uint8_t)(((length & 0x07) << 5) | 0x1F); /* buffer fullness 0x7FF */
    header[6] = 0xFC;
}

static int write_encoded(AVCodecContext* encoder, AVFrame* frame, FILE* file, int adts) {
    int rc = avcodec_send_frame(encoder, frame);
    AVPacket* packet = av_packet_alloc();
    if (!packet) return AVERROR(ENOMEM);
    while (rc >= 0) {
        rc = avcodec_receive_packet(encoder, packet);
        if (rc == AVERROR(EAGAIN) || rc == AVERROR_EOF) { rc = 0; break; }
        if (rc < 0) break;
        if (adts) {
            uint8_t header[7];
            write_adts_header(header, packet->size);
            if (fwrite(header, 1, sizeof(header), file) != sizeof(header)) { rc = AVERROR(EIO); break; }
        }
        if (fwrite(packet->data, 1, (size_t)packet->size, file) != (size_t)packet->size) { rc = AVERROR(EIO); break; }
        av_packet_unref(packet);
    }
    av_packet_free(&packet);
    return rc;
}

static void close_video_output(VideoOutput* output) {
    if (output->encoder && output->file) write_encoded(output->encoder, NULL, output->file, 0);
    if (output->file) fclose(output->file);
    sws_freeContext(output->scaler);
    av_frame_free(&output->frame);
    avcodec_free_context(&output->encoder);
}

typedef struct AudioOutput {
    AVCodecContext* encoder;
    SwrContext* resampler;
    AVAudioFifo* fifo;
    FILE* file;
    AVFrame* frame;
    int64_t pts;
    int frame_size;
    int adts;
} AudioOutput;

static int open_audio_output(AudioOutput* output, AVCodecContext* decoder, const char* codec_name,
    const char* path, enum AVSampleFormat format, int frame_size, int bitrate) {
    memset(output, 0, sizeof(*output));
    const AVCodec* codec = avcodec_find_encoder_by_name(codec_name);
    if (!codec) return AVERROR_ENCODER_NOT_FOUND;
    output->encoder = avcodec_alloc_context3(codec);
    if (!output->encoder) return AVERROR(ENOMEM);
    output->encoder->sample_rate = 8000;
    output->encoder->sample_fmt = format;
    output->encoder->time_base = (AVRational){1, 8000};
    output->encoder->bit_rate = bitrate;
    av_channel_layout_default(&output->encoder->ch_layout, 1);
    int rc = avcodec_open2(output->encoder, codec, NULL);
    if (rc < 0) return rc;
    output->file = fopen(path, "wb");
    if (!output->file) return AVERROR(errno);
    AVChannelLayout mono;
    av_channel_layout_default(&mono, 1);
    rc = swr_alloc_set_opts2(&output->resampler, &mono, format, 8000,
        &decoder->ch_layout, decoder->sample_fmt, decoder->sample_rate, 0, NULL);
    av_channel_layout_uninit(&mono);
    if (rc >= 0) rc = swr_init(output->resampler);
    if (rc < 0) return rc;
    output->frame = av_frame_alloc();
    if (!output->frame) return AVERROR(ENOMEM);
    output->frame->format = format;
    output->frame->sample_rate = 8000;
    av_channel_layout_default(&output->frame->ch_layout, 1);
    output->frame_size = frame_size > 0 ? frame_size : 1024;
    output->adts = codec_name && strcmp(codec_name, "aac") == 0;
    output->frame->nb_samples = output->frame_size;
    rc = av_frame_get_buffer(output->frame, 0);
    if (rc < 0) return rc;
    output->fifo = av_audio_fifo_alloc(format, 1, output->frame_size * 8);
    if (!output->fifo) return AVERROR(ENOMEM);
    return rc;
}

static int convert_audio(AudioOutput* output, AVFrame* input) {
    int target = (int)av_rescale_rnd(swr_get_delay(output->resampler, input->sample_rate) + input->nb_samples,
        8000, input->sample_rate, AV_ROUND_UP);
    int needed = target > output->frame_size ? target : output->frame_size;
    if (needed > output->frame->nb_samples) {
        av_frame_unref(output->frame);
        output->frame->nb_samples = needed;
        output->frame->format = output->encoder->sample_fmt;
        output->frame->sample_rate = 8000;
        av_channel_layout_copy(&output->frame->ch_layout, &output->encoder->ch_layout);
        int rc = av_frame_get_buffer(output->frame, 0);
        if (rc < 0) return rc;
    }
    int rc = av_frame_make_writable(output->frame);
    if (rc < 0) return rc;
    rc = swr_convert(output->resampler, output->frame->data, output->frame->nb_samples,
        (const uint8_t**)input->extended_data, input->nb_samples);
    if (rc < 0) return rc;
    output->frame->nb_samples = rc;
    rc = av_audio_fifo_write(output->fifo, (void* const*)output->frame->extended_data, rc);
    if (rc < 0) return rc;
    while (av_audio_fifo_size(output->fifo) >= output->frame_size) {
        rc = av_audio_fifo_read(output->fifo, (void* const*)output->frame->data, output->frame_size);
        if (rc < 0) return rc;
        output->frame->nb_samples = rc;
        output->frame->pts = output->pts;
        output->pts += rc;
        rc = write_encoded(output->encoder, output->frame, output->file, output->adts);
        if (rc < 0) return rc;
    }
    return 0;
}

static void close_audio_output(AudioOutput* output) {
    if (output->encoder && output->file) {
        if (output->resampler && output->fifo) {
            int drained = 0;
            do {
                av_frame_make_writable(output->frame);
                output->frame->nb_samples = output->frame_size;
                drained = swr_convert(output->resampler, output->frame->data, output->frame->nb_samples, NULL, 0);
                if (drained > 0) {
                    output->frame->nb_samples = drained;
                    av_audio_fifo_write(output->fifo, (void* const*)output->frame->extended_data, drained);
                }
            } while (drained > 0);
        }
        if (output->fifo) {
            while (av_audio_fifo_size(output->fifo) > 0) {
                int count = av_audio_fifo_size(output->fifo);
                if (count > output->frame_size) count = output->frame_size;
                av_audio_fifo_read(output->fifo, (void* const*)output->frame->data, count);
                output->frame->nb_samples = count;
                output->frame->pts = output->pts;
                output->pts += count;
                write_encoded(output->encoder, output->frame, output->file, output->adts);
            }
        }
        write_encoded(output->encoder, NULL, output->file, output->adts);
    }
    if (output->file) fclose(output->file);
    av_audio_fifo_free(output->fifo);
    swr_free(&output->resampler);
    av_frame_free(&output->frame);
    avcodec_free_context(&output->encoder);
}

TF_EXPORT int __cdecl tf_transcode(const wchar_t* input_path, const wchar_t* h264_output_path,
    const wchar_t* g711a_output_path, const wchar_t* aac_output_path, double frame_rate,
    int gop_size, int max_bitrate, int buffer_size, tf_progress_callback callback, void* user_data) {
    char *input_name=utf8(input_path), *video_name=utf8(h264_output_path),
         *g711_name=utf8(g711a_output_path), *aac_name=utf8(aac_output_path);
    AVFormatContext* input=NULL; AVCodecContext *video_decoder=NULL,*audio_decoder=NULL;
    VideoOutput video={0}; AudioOutput g711={0},aac={0}; AVPacket* packet=NULL; AVFrame* decoded=NULL;
    int rc=avformat_open_input(&input,input_name,NULL,NULL), video_index=-1,audio_index=-1;
    if(rc>=0) rc=avformat_find_stream_info(input,NULL);
    if(rc>=0) { video_index=av_find_best_stream(input,AVMEDIA_TYPE_VIDEO,-1,-1,NULL,0); rc=video_index; }
    if(rc>=0) rc=open_decoder(input,video_index,&video_decoder);
    if(rc>=0) rc=open_video_output(&video,video_decoder,video_name,frame_rate,gop_size,max_bitrate,buffer_size);
    audio_index=av_find_best_stream(input,AVMEDIA_TYPE_AUDIO,-1,-1,NULL,0);
    if(rc>=0 && audio_index>=0 && (g711_name||aac_name)) rc=open_decoder(input,audio_index,&audio_decoder);
    if(rc>=0 && audio_decoder && g711_name) rc=open_audio_output(&g711,audio_decoder,"pcm_alaw",g711_name,AV_SAMPLE_FMT_S16,320,64000);
    if(rc>=0 && audio_decoder && aac_name) rc=open_audio_output(&aac,audio_decoder,"aac",aac_name,AV_SAMPLE_FMT_FLTP,1024,64000);
    packet=av_packet_alloc(); decoded=av_frame_alloc(); if(!packet||!decoded) rc=AVERROR(ENOMEM);
    while(rc>=0 && av_read_frame(input,packet)>=0) {
        AVCodecContext* decoder=packet->stream_index==video_index?video_decoder:(packet->stream_index==audio_index?audio_decoder:NULL);
        if(decoder) {
            rc=avcodec_send_packet(decoder,packet);
            while(rc>=0) {
                rc=avcodec_receive_frame(decoder,decoded);
                if(rc==AVERROR(EAGAIN)||rc==AVERROR_EOF){rc=0;break;} if(rc<0)break;
                if(decoder==video_decoder){
                    av_frame_make_writable(video.frame);
                    sws_scale(video.scaler,(const uint8_t* const*)decoded->data,decoded->linesize,0,decoded->height,video.frame->data,video.frame->linesize);
                    video.frame->pts=video.pts++; rc=write_encoded(video.encoder,video.frame,video.file,0);
                    if(callback && input->duration>0) callback((double)decoded->best_effort_timestamp*av_q2d(input->streams[video_index]->time_base)/(input->duration/(double)AV_TIME_BASE),user_data);
                } else { if(g711.encoder)rc=convert_audio(&g711,decoded); if(rc>=0&&aac.encoder)rc=convert_audio(&aac,decoded); }
                av_frame_unref(decoded);
            }
        }
        av_packet_unref(packet);
    }
    if(rc==AVERROR_EOF)rc=0;
    av_packet_free(&packet);av_frame_free(&decoded);close_video_output(&video);close_audio_output(&g711);close_audio_output(&aac);
    avcodec_free_context(&video_decoder);avcodec_free_context(&audio_decoder);avformat_close_input(&input);
    av_free(input_name);av_free(video_name);av_free(g711_name);av_free(aac_name);
    return rc<0?fail(rc,L"进程内转码"):0;
}

TF_EXPORT int __cdecl tf_thumbnail(const wchar_t* input_path, const wchar_t* output_path) {
    char* input_name=utf8(input_path); char* output_name=utf8(output_path);
    AVFormatContext* input=NULL; AVCodecContext *decoder=NULL,*encoder=NULL;
    AVPacket *packet=av_packet_alloc(),*encoded=av_packet_alloc();
    AVFrame *decoded=av_frame_alloc(),*target=av_frame_alloc();
    struct SwsContext* scaler=NULL; FILE* output=NULL; int video_index=-1,rc=0,done=0;
    if(!input_name||!output_name||!packet||!encoded||!decoded||!target){rc=AVERROR(ENOMEM);goto cleanup;}
    rc=avformat_open_input(&input,input_name,NULL,NULL); if(rc<0)goto cleanup;
    rc=avformat_find_stream_info(input,NULL); if(rc<0)goto cleanup;
    video_index=av_find_best_stream(input,AVMEDIA_TYPE_VIDEO,-1,-1,NULL,0); if(video_index<0){rc=video_index;goto cleanup;}
    rc=open_decoder(input,video_index,&decoder); if(rc<0)goto cleanup;
    const AVCodec* codec=avcodec_find_encoder(AV_CODEC_ID_MJPEG); if(!codec){rc=AVERROR_ENCODER_NOT_FOUND;goto cleanup;}
    encoder=avcodec_alloc_context3(codec); if(!encoder){rc=AVERROR(ENOMEM);goto cleanup;}
    encoder->width=decoder->width; encoder->height=decoder->height; encoder->pix_fmt=AV_PIX_FMT_YUVJ420P;
    encoder->time_base=(AVRational){1,25}; rc=avcodec_open2(encoder,codec,NULL); if(rc<0)goto cleanup;
    target->format=encoder->pix_fmt; target->width=encoder->width; target->height=encoder->height;
    rc=av_frame_get_buffer(target,32); if(rc<0)goto cleanup;
    scaler=sws_getContext(decoder->width,decoder->height,decoder->pix_fmt,encoder->width,encoder->height,
        encoder->pix_fmt,SWS_BILINEAR,NULL,NULL,NULL); if(!scaler){rc=AVERROR(EINVAL);goto cleanup;}
    output=fopen(output_name,"wb"); if(!output){rc=AVERROR(errno);goto cleanup;}
    while(!done && av_read_frame(input,packet)>=0){
        if(packet->stream_index==video_index){
            rc=avcodec_send_packet(decoder,packet);
            while(rc>=0){
                rc=avcodec_receive_frame(decoder,decoded);
                if(rc==AVERROR(EAGAIN)||rc==AVERROR_EOF){rc=0;break;} if(rc<0)goto cleanup;
                av_frame_make_writable(target);
                sws_scale(scaler,(const uint8_t* const*)decoded->data,decoded->linesize,0,decoded->height,target->data,target->linesize);
                rc=avcodec_send_frame(encoder,target); if(rc<0)goto cleanup;
                rc=avcodec_receive_packet(encoder,encoded); if(rc<0)goto cleanup;
                if(fwrite(encoded->data,1,(size_t)encoded->size,output)!=(size_t)encoded->size){rc=AVERROR(EIO);goto cleanup;}
                done=1;rc=0;break;
            }
        }
        av_packet_unref(packet);
    }
    if(!done&&rc>=0)rc=AVERROR_INVALIDDATA;
cleanup:
    if(output)fclose(output);sws_freeContext(scaler);av_packet_free(&packet);av_packet_free(&encoded);
    av_frame_free(&decoded);av_frame_free(&target);avcodec_free_context(&decoder);avcodec_free_context(&encoder);
    avformat_close_input(&input);av_free(input_name);av_free(output_name);
    return rc<0?fail(rc,L"生成缩略图"):0;
}

TF_EXPORT void __cdecl tf_last_error(wchar_t* buffer, int capacity) {
    if(buffer&&capacity>0) wcsncpy_s(buffer,(size_t)capacity,last_error,_TRUNCATE);
}
