// Import necessary libraries
import { Component } from '@angular/core';
import jsPDF from 'jspdf';
import html2canvas from 'html2canvas';

@Component({
  selector: 'app-pdf-generator',
  templateUrl: './pdf-generator.component.html',
  styleUrls: ['./pdf-generator.component.css']
})
export class PdfGeneratorComponent {

  constructor() { }

  // Method to capture HTML and convert it into a PDF in landscape orientation
  public downloadPdf(): void {
    const data = document.getElementById('contentToConvert') as HTMLElement;

    html2canvas(data, { scale: 1 }).then(canvas => {
      const contentDataURL = canvas.toDataURL('image/png');
      let pdf = new jsPDF('l', 'mm', 'a4'); // Initialize jsPDF in landscape mode
      let pdfWidth = pdf.internal.pageSize.getWidth();
      let pdfHeight = pdf.internal.pageSize.getHeight();

      let imgWidth = pdfWidth;
      let imgHeight = canvas.height * pdfWidth / canvas.width;
      let position = 0;

      // Add image to the first page
      pdf.addImage(contentDataURL, 'PNG', 0, position, imgWidth, imgHeight);
      
      let heightLeft = imgHeight - pdfHeight;

      // Add new pages if the content overflows
      while (heightLeft > 0) {
        position = -(imgHeight - heightLeft);
        pdf.addPage();
        pdf.addImage(contentDataURL, 'PNG', 0, position, imgWidth, imgHeight);
        heightLeft -= pdfHeight;
      }

      pdf.save('download.pdf');
    });
  }
}
