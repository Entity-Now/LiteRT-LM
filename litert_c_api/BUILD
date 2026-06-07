package(default_visibility = ["//visibility:public"])

cc_binary(
    name = "litert_lm",
    srcs = ["win_dummy.cc"],
    deps = [
        "//c:engine",
    ],
    linkshared = True,

    # ==================== Windows 专属配置 ====================
    win_def_file = select({
        "@platforms//os:windows": "litert_lm.def",
        "//conditions:default": None,
    }),

    # 如果你还想额外传递 link 选项（可选）
    linkopts = select({
        "@platforms//os:windows": [],
        "//conditions:default": [],
    }),
)
