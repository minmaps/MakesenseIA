#pragma once

#include <cstddef>
#include <list>
#include <string>
#include <unordered_map>

class budgeted_lru
{
public:
    void set_limit(std::size_t bytes)
    {
        limit_bytes_ = bytes;
        trim();
    }

    void touch(const std::wstring& key, std::size_t bytes)
    {
        auto entry = entries_.find(key);
        if (entry != entries_.end())
        {
            current_bytes_ -= entry->second->second;
            order_.erase(entry->second);
            entries_.erase(entry);
        }

        order_.emplace_front(key, bytes);
        entries_[key] = order_.begin();
        current_bytes_ += bytes;
        trim();
    }

    [[nodiscard]] std::size_t current_bytes() const noexcept
    {
        return current_bytes_;
    }

private:
    void trim()
    {
        while (current_bytes_ > limit_bytes_ && !order_.empty())
        {
            const auto& tail = order_.back();
            current_bytes_ -= tail.second;
            entries_.erase(tail.first);
            order_.pop_back();
        }
    }

    std::size_t limit_bytes_{};
    std::size_t current_bytes_{};
    std::list<std::pair<std::wstring, std::size_t>> order_;
    std::unordered_map<std::wstring, std::list<std::pair<std::wstring, std::size_t>>::iterator> entries_;
};
