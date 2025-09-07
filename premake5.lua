-- Solution
workspace "VeloraBackend"
    architecture "x64"
    startproject "VeloraServer"

    configurations
    {
        "Debug",
        "Release"
    }

    outputdir = "%{cfg.buildcfg}-%{cfg.system}-%{cfg.architecture}"

-- VeloraServer
project "VeloraServer"
    location "VeloraServer"
    kind "ConsoleApp"
    language "C#"
    dotnetframework "net8.0"
    
    targetdir ("bin/" .. outputdir .. "/%{prj.name}")
    objdir ("bin-int/" .. outputdir .. "/%{prj.name}")

    nuget
    {
        "Serilog:3.1.1",
        "Serilog.Sinks.Console:4.1.0",
        "Serilog.Sinks.File:5.0.0"
    }

    files
    {
        "%{prj.name}/Src/**.cs",
        "%{prj.name}/%{prj.name}.csproj"
    }

    filter "configurations:Debug"
        runtime "Debug"
        symbols "on"

    filter "configurations:Release"
        runtime "Release"
        optimize "on"
