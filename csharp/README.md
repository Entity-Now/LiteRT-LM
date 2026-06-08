# LiteRT-LM C# SDK 官方使用文档

## 1. 项目介绍
LiteRT-LM C# SDK 是 Google LiteRT（原 Mediapipe LLM Inference）大语言模型推理引擎的 C# 原生封装包。它通过 P/Invoke 技术直接调用底层 C/C++ 的 `libLiteRt` 动态链接库，旨在为 .NET 开发者提供高性能、低延迟的端侧（On-Device）大语言模型和多模态模型推理能力。支持 Windows、Linux、macOS 等主流操作系统。

通过此 SDK，您可以在 C# 应用程序中轻松集成 LLM（如 Gemma、Llama 等），并支持文本生成、多轮对话、流式输出、工具调用（Function Calling）以及多模态（图像、音频）输入等高级特性。

## 2. 架构说明
SDK 主要分为两层架构：
* **Native Interop 层 (`LiteRtLmNative.cs`)**：负责声明与底层 `litert_lm_c_api` 交互的 `[DllImport]` 外部函数，处理内存分配、指针转换及回调函数的编解码。
* **C# 高级 API 层**：提供面向对象的安全封装，主要包含：
  * `Engine`：引擎生命周期管理类，负责加载模型权重、配置后端加速（CPU/GPU/NPU）、管理 LoRA 权重及初始化推理环境。
  * `Conversation`：对话会话管理类，负责维护上下文、发送和接收多模态消息、处理流式回调和异步执行工具调用。
  * `Message` / `Content`：抽象的消息及多模态内容体。
  * `ToolManager`：负责绑定并分发大模型工具（Function）调用请求。

## 3. 技术栈
* **开发语言**：C# (.NET Standard / .NET 6.0+)
* **底层依赖**：C/C++ (LiteRT-LM Native Engine)
* **核心技术**：P/Invoke, 异步编程 (async/await), `IAsyncEnumerable<T>` (流式处理), JSON 序列化 (`System.Text.Json`)

## 4. 接口/函数/类型列表

### 核心配置类
* **`EngineConfig`**
  * `ModelPath` (string): 本地模型文件路径（必须提供）。
  * `Backend` (Backend): 计算后端配置（如 CPU、GPU 等）。
  * `MaxNumTokens` (int?): 允许的最大 Token 数。
  * `CacheDir` (string): 缓存目录路径。
  * `LoraRank` / `AudioLoraRank` (int?): LoRA 权重秩数配置。
* **`ConversationConfig`**
  * `SystemMessage` (Message): 系统提示词。
  * `InitialMessages` (List<Message>): 初始历史会话消息。
  * `Tools` (List<ITool>): 注册供模型调用的工具列表。
  * `SamplerConfig` (SamplerConfig): 采样器配置（TopK, TopP, Temperature, Seed）。
  * `LoraPath` / `AudioLoraPath` (string): LoRA 适配器路径。

### 核心业务类
* **`Engine` (引擎实例，需 `Dispose`)**
  * `Initialize()`: 初始化引擎，加载模型至内存。
  * `Conversation CreateConversation(ConversationConfig config = null)`: 创建一个包含独立上下文的对话实例。
* **`Conversation` (会话实例，需 `Dispose`)**
  * `Task<Message> SendMessage(Message message, Dictionary<string, object> extraContext = null)`: 发送消息并等待完整回复。
  * `IAsyncEnumerable<Message> SendMessageStream(Message message, Dictionary<string, object> extraContext = null)`: 发送消息并以流式（Streaming）形式返回回复块。
  * `string RenderMessageIntoString(Message message)`: 将消息对象渲染成字符串（根据模型模板）。
  * `void Cancel()`: 中断当前正在进行的推理任务。
  * `int GetTokenCount()`: 获取当前对话的上下文 Token 数量。
  * `BenchmarkInfo GetBenchmarkInfo()`: 获取性能评测数据（需在初始化前开启实验性开关）。

### 消息体类型
* **`Role` (枚举)**: `System`, `User`, `Model`, `Tool`
* **`Message`**: 包含发送者角色 `Role` 及内容列表 `Contents` 的消息对象。
* **`Content` (基类)**
  * `TextContent`: 文本内容。
  * `ImageDataContent` / `ImageFileContent`: 图像数据/文件内容。
  * `AudioDataContent` / `AudioFileContent`: 音频数据/文件内容。

---

## 5. 使用案例

### 5.1 基础文本生成 (String Generation)
用于一次性的文本问答或指令执行（不保持多轮上下文）。

```csharp
using System;
using LiteRTLM.Core;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        // 1. 配置并初始化引擎
        var engineConfig = new EngineConfig("path/to/your/model.bin");
        using var engine = new Engine(engineConfig);
        engine.Initialize();

        // 2. 创建会话（即便是单次对话也需要会话实例）
        using var conversation = engine.CreateConversation();
        
        // 3. 构造请求并发送
        var request = new Message("What is the capital of France?");
        var response = await conversation.SendMessage(request);
        
        Console.WriteLine($"Model: {response.Text}");
    }
}
```

### 5.2 多轮聊天 (Chat / Conversation)
模型能够记住之前的对话历史并做出连贯的回复。

```csharp
using System;
using LiteRTLM.Core;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        // 1. 初始化引擎
        var engineConfig = new EngineConfig("path/to/your/model.bin");
        using var engine = new Engine(engineConfig);
        engine.Initialize();

        // 2. 注入 System Message，创建多轮对话实例
        var conversationConfig = new ConversationConfig(
            systemMessage: new Message("You are a helpful and concise AI assistant.", Role.System)
        );
        using var conversation = engine.CreateConversation(conversationConfig);

        // 第一轮对话
        Console.WriteLine("User: Hello! I'm a developer.");
        var response1 = await conversation.SendMessage(new Message("Hello! I'm a developer."));
        Console.WriteLine($"Model: {response1.Text}");

        // 第二轮对话（模型会结合上下文）
        Console.WriteLine("User: What is my profession?");
        var response2 = await conversation.SendMessage(new Message("What is my profession?"));
        Console.WriteLine($"Model: {response2.Text}");
    }
}
```

### 5.3 流式输出 (Stream Generation)
适用于生成长文本时，将生成的词块实时打印到控制台，从而极大优化用户的首字等待时间（TTFT）。

```csharp
using System;
using LiteRTLM.Core;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        // 1. 初始化引擎
        var engineConfig = new EngineConfig("path/to/your/model.bin");
        using var engine = new Engine(engineConfig);
        engine.Initialize();

        // 2. 创建会话
        using var conversation = engine.CreateConversation();

        Console.Write("Model: ");
        
        // 3. 使用 SendMessageStream 监听异步流
        var request = new Message("Tell me a long story about a brave knight.");
        await foreach (var chunk in conversation.SendMessageStream(request))
        {
            // 实时输出生成的文本块
            Console.Write(chunk.Text);
        }
        Console.WriteLine();
    }
}
```

### 5.4 进阶用法：设置采样器与硬件加速后端

您可以配置 `SamplerConfig` 控制生成内容的随机性，或者指定 GPU 后端来加速推理。

```csharp
using LiteRTLM.Core;

// 指定后端加速（例如使用 GPU，如果支持）
var engineConfig = new EngineConfig(
    modelPath: "path/to/your/model.bin",
    backend: Backend.Gpu() // 或 Backend.Cpu() 等
);

using var engine = new Engine(engineConfig);
engine.Initialize();

// 配置采样器（Temperature 越低内容越确定，越高越具创造性）
var conversationConfig = new ConversationConfig(
    samplerConfig: new SamplerConfig
    {
        Temperature = 0.7f,
        TopK = 40,
        TopP = 0.9f,
        Seed = 42
    }
);

using var conversation = engine.CreateConversation(conversationConfig);
// 随后开始生成...
```
