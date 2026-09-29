window.downloadFileFromStream = async (fileName, contentStreamReference) => {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer]);
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

// SimpleBlazorMonaco never disposes its editors, so release any whose host left the DOM.
(() => {
    const editors = new Set();
    let monacoInstance;

    const disposeDetachedEditors = () => {
        for (const editor of editors) {
            if (editor.getDomNode()?.isConnected) continue;
            const model = editor.getModel();
            editor.dispose();
            if (model && !model.isDisposed() && !model.isAttachedToEditor()) {
                model.dispose();
            }
            editors.delete(editor);
        }
    };

    Object.defineProperty(window, 'monaco', {
        configurable: true,
        get: () => monacoInstance,
        set: (value) => {
            monacoInstance = value;
            value?.editor?.onDidCreateEditor((editor) => {
                disposeDetachedEditors();
                editors.add(editor);
            });
        }
    });
})();
