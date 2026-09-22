# 内置图标素材

| 名称 | 注册后缀 | 编译资源 | 源素材 |
| --- | --- | --- | --- |
| Julia | .jl | julia/julia.ico | julia/julia.svg |
| Typst | .typ | typst/typst.ico | typst/typst.png |

`catalog.json` 定义界面中的素材名称与后缀。构建脚本和项目文件将两份 ICO 与目录一起嵌入 EXE，软件不依赖本机原目录。

Julia SVG 此前取自 [JuliaLang/julia 的 contrib/julia.svg](https://github.com/JuliaLang/julia/blob/master/contrib/julia.svg)，ICO 为本地转换版本。Typst PNG 和 ICO 沿用用户已有素材；本次没有验证 PNG 的最初来源或重新授予其使用许可。项目名称和标识的相关权利属于各自权利人。

新增内置图标时：添加素材、更新 catalog.json，并在 scripts/build.ps1 和 IconController.csproj 中登记相同的资源名。
