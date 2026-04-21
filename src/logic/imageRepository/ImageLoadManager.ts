export class ImageLoadManager {

	private static queue: Array<() => Promise<unknown>> = [];
	private static isRunning: boolean = false;
	private static isScheduled: boolean = false;

	public static add(task: () => Promise<unknown>) {
		ImageLoadManager.queue.push(task);
	}

	public static run() {
		if (ImageLoadManager.isScheduled) {
			return;
		}

		ImageLoadManager.isScheduled = true;
		setTimeout(() => {
			ImageLoadManager.isScheduled = false;
			void ImageLoadManager.runQueue();
		}, 10);
	}

	public static addAndRun(task: () => Promise<unknown>) {
		ImageLoadManager.add(task);
		ImageLoadManager.run();
	}

	public static async runQueue() {
		if (!ImageLoadManager.isRunning) {
			ImageLoadManager.isRunning = true;
			try {
				await ImageLoadManager.runTasks();
			} finally {
				ImageLoadManager.isRunning = false;
			}
		}
	}

	private static async runTasks() {
		while (ImageLoadManager.queue.length > 0) {
			const task = ImageLoadManager.queue.shift();
			if (task) {
				await task();
			}
		}
	}
}
