# monojni — the .NET host, built as an ordinary JNI library.
#
# This used to build an executable that was named like a library, because
# lib/<abi>/ is the only place in an installed app that is executable without
# targeting API 28. That trick is gone along with the reason for it: the
# runtime needs a JavaVM to do cryptography on Android (see monojni.c), so it
# is loaded into an app process by System.loadLibrary rather than exec'd, and
# a library is what a library should be.

cmake_minimum_required(VERSION 3.22.1)
project(monojni C)

add_library(monojni SHARED monojni.c)

# Loadable on a device with 16 KB pages.
#
# Android 15 allows them, and a shared library whose segments are aligned for
# 4 KB cannot be mapped on one -- it fails to load, which here means the whole
# build step fails on a device that is otherwise perfectly good. Everything
# else in lib/arm64-v8a already comes aligned this way, including all of the
# runtime's own libraries; this is the one built here, so it is the one that
# has to ask.
target_link_options(monojni PRIVATE "-Wl,-z,max-page-size=16384")

# log for __android_log_print, dl for the runtime itself.
target_link_libraries(monojni log dl)
