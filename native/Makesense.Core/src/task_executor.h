#pragma once

#include <condition_variable>
#include <cstddef>
#include <functional>
#include <mutex>
#include <queue>
#include <thread>
#include <vector>

class task_executor
{
public:
    task_executor() = default;
    ~task_executor();

    void configure(std::size_t thread_count);
    void submit(std::function<void()> task);
    void shutdown(bool discard_pending = false);

private:
    void worker_loop();

    std::mutex mutex_;
    std::condition_variable condition_;
    std::queue<std::function<void()>> tasks_;
    std::vector<std::thread> threads_;
    bool stopping_{false};
};
