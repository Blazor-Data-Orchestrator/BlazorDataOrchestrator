// Force Monaco editor to re-layout after programmatic code updates
window.forceMonacoLayout = function () {
    if (typeof monaco !== 'undefined' && monaco.editor) {
        var editors = monaco.editor.getEditors();
        if (editors && editors.length > 0) {
            editors.forEach(function (editor) {
                editor.layout();
            });
        }
    }
};
