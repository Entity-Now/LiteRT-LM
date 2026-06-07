package(default_visibility = ["//visibility:public"])

# 针对 Windows 进行特定配置，Mac 则自动跳过 DEF
outputs = select({
    "@bazel_tools//src/conditions:windows": ["/DEF:$(location :litert_lm.def)"],
    "//conditions:default": [],
})

cc_binary(
    name = "litert_lm",
    srcs = [
        "win_dummy.cc",
        "litert_lm.def",
    ],
    linkopts = outputs,
    linkshared = True,
    deps = [
        "//c:engine", 
    ],
)
