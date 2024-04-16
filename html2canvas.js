import jsPDF from 'jspdf';
import html2canvas from 'html2canvas';

const input = document.getElementById('yourElementId'); // The element you want to print

html2canvas(input, { scale: 1 }).then(canvas => {
    const imgData = canvas.toDataURL('image/png');
    const pdf = new jsPDF({
        orientation: 'portrait',
        unit: 'px',
        format: [canvas.width, canvas.height]
    });

    let pageHeight = pdf.internal.pageSize.height;
    let imgHeight = canvas.height;
    let heightLeft = imgHeight;
    let position = 0;

    pdf.addImage(imgData, 'PNG', 0, position, canvas.width, canvas.height);
    heightLeft -= pageHeight;

    while (heightLeft >= 0) {
        position = heightLeft - imgHeight;
        pdf.addPage();
        pdf.addImage(imgData, 'PNG', 0, position, canvas.width, canvas.height);
        heightLeft -= pageHeight;
    }

    pdf.save('download.pdf');
});
