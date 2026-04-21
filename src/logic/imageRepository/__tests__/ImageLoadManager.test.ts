import { ImageLoadManager } from "../ImageLoadManager";

const flushTimers = async () => {
    jest.runOnlyPendingTimers();
    await Promise.resolve();
    await Promise.resolve();
};

describe("ImageLoadManager", () => {
    beforeEach(() => {
        jest.useFakeTimers();
    });

    afterEach(() => {
        jest.runOnlyPendingTimers();
        jest.useRealTimers();
    });

    it("runs queued tasks sequentially", async () => {
        const executionOrder: string[] = [];
        let releaseFirstTask: (() => void) | null = null;

        const firstTask = jest.fn(() => new Promise<void>((resolve) => {
            executionOrder.push("first-start");
            releaseFirstTask = () => {
                executionOrder.push("first-end");
                resolve();
            };
        }));

        const secondTask = jest.fn(async () => {
            executionOrder.push("second");
        });

        ImageLoadManager.addAndRun(firstTask);
        ImageLoadManager.addAndRun(secondTask);

        await flushTimers();

        expect(firstTask).toHaveBeenCalledTimes(1);
        expect(secondTask).not.toHaveBeenCalled();
        expect(executionOrder).toEqual(["first-start"]);

        releaseFirstTask?.();
        await Promise.resolve();
        await Promise.resolve();

        expect(secondTask).toHaveBeenCalledTimes(1);
        expect(executionOrder).toEqual(["first-start", "first-end", "second"]);
    });
});
