#include "task_executor.h"

task_executor::~task_executor()
{
    shutdown();
}

void task_executor::configure(std::size_t thread_count)
{
    shutdown();
    stopping_ = false;

    for (std::size_t index = 0; index < thread_count; ++index)
    {
        threads_.emplace_back([this]
        {
            worker_loop();
        });
    }
}

void task_executor::submit(std::function<void()> task)
{
    {
        std::lock_guard lock(mutex_);
        if (stopping_)
        {
            return;
        }

        tasks_.push(std::move(task));
    }

    condition_.notify_one();
}

void task_executor::shutdown()
{
    {
        std::lock_guard lock(mutex_);
        stopping_ = true;
    }

    condition_.notify_all();

    for (auto& thread : threads_)
    {
        if (thread.joinable())
        {
            thread.join();
        }
    }

    threads_.clear();

    std::queue<std::function<void()>> empty;
    {
        std::lock_guard lock(mutex_);
        std::swap(tasks_, empty);
    }
}

void task_executor::worker_loop()
{
    for (;;)
    {
        std::function<void()> task;
        {
            std::unique_lock lock(mutex_);
            condition_.wait(lock, [this]
            {
                return stopping_ || !tasks_.empty();
            });

            if (stopping_ && tasks_.empty())
            {
                return;
            }

            task = std::move(tasks_.front());
            tasks_.pop();
        }

        task();
    }
}
